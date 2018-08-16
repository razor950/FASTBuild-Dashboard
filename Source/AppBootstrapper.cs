using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Timers;
using Caliburn.Micro;
using FastBuild.Dashboard.Configuration;
using FastBuild.Dashboard.Services;
using FastBuild.Dashboard.Services.Build;
using FastBuild.Dashboard.Services.Build.SourceEditor;
using FastBuild.Dashboard.Services.Worker;
using FastBuild.Dashboard.Support;
using FastBuild.Dashboard.ViewModels;

namespace FastBuild.Dashboard
{
	internal class AppBootstrapper : BootstrapperBase
	{
		private readonly SimpleContainer _container = new SimpleContainer();

		public AppBootstrapper()
		{
			this.Initialize();
		}

		protected override void Configure()
		{
			base.Configure();
			_container.Singleton<IWindowManager, WindowManager>();
			_container.Singleton<IEventAggregator, EventAggregator>();
			_container.Singleton<MainWindowViewModel>();
			_container.Singleton<IBuildViewportService, BuildViewportService>();
			_container.Singleton<IBrokerageService, BrokerageService>();
			_container.Singleton<IWorkerAgentService, WorkerAgentService>();
			_container.Singleton<IExternalSourceEditorService, ExternalSourceEditorService>();
		}

		protected override void OnStartup(object sender, StartupEventArgs e)
		{		
			if (!App.Current.IsShadowProcess)
			{
				// a shadow process is always started by a non-shadow process, which
				// should already have the startup registry value set
				App.Current.SetStartupWithWindows(AppSettings.Default.StartWithWindows);
			}

#if DEBUG && !DEBUG_SINGLE_INSTANCE
			this.DisplayRootViewFor<MainWindowViewModel>();
			return;
#else
			var assemblyLocation = Assembly.GetEntryAssembly().Location;

			var identifier = assemblyLocation.Replace('\\', '_');
			if (!SingleInstance<App>.InitializeAsFirstInstance(identifier))
			{
				Environment.Exit(0);
			}

			if (App.Current.DoNotSpawnShadowExecutable || App.Current.IsShadowProcess)
			{
				if (App.Current.IsShadowProcess)
				{
					AppBootstrapper.UpdateOriginal(e);
					var checkTimer = new Timer(1000*60*15); // Check for changes every 15 minutes.
					checkTimer.Elapsed += (senderTimer, eTimer) => AppBootstrapper.UpdateOriginal(e);
					checkTimer.AutoReset = true;
					checkTimer.Enabled = true;
				}
				this.DisplayRootViewFor<MainWindowViewModel>();
			}
			else
			{
				AppBootstrapper.SpawnShadowProcess(e, assemblyLocation);
				Environment.Exit(0);
			}
#endif
		}

		private static void CreateShadowContext(string shadowPath)
		{
			var shadowContext = new ShadowContext();
			shadowContext.Save(shadowPath);
		}

		private static void UpdateOriginal(StartupEventArgs e)
		{
			var brokeragePath = IoC.Get<IBrokerageService>().BrokeragePath;
			var networkCheckPath = Path.Combine(brokeragePath, "FBDashboard", "FBDashboard.exe");
			var originalProcPath = App.Current.ShadowContext.OriginalLocation;

			try
			{
				bool filesChanged = false;

				if (File.Exists(networkCheckPath))
				{
					bool bFilesAreEqual = new FileInfo(networkCheckPath).Length == new FileInfo(originalProcPath).Length &&
						File.ReadAllBytes(networkCheckPath).SequenceEqual(File.ReadAllBytes(originalProcPath));
					// If files are not equal. Copy newer version from network location over original file.
					if (!bFilesAreEqual)
					{
						if (File.Exists(originalProcPath))
						{
							File.Delete(originalProcPath);
						}
						File.Copy(networkCheckPath, originalProcPath);
						filesChanged = true;
					}

					var workerNetworkPath = Path.Combine(Path.GetDirectoryName(networkCheckPath), "FBuild", "FBuildWorker.exe");
					var workerTargetPath = Path.Combine(Path.GetDirectoryName(originalProcPath), "FBuild", "FBuildWorker.exe");

					if (File.Exists(workerNetworkPath) && File.Exists(workerTargetPath))
					{
						bool bFilesAreEqualWorker = new FileInfo(workerNetworkPath).Length == new FileInfo(workerTargetPath).Length &&
						File.ReadAllBytes(workerNetworkPath).SequenceEqual(File.ReadAllBytes(workerTargetPath));
						// If files are not equal. Copy newer version from network location over original file.
						if (!bFilesAreEqualWorker)
						{
							File.Delete(workerTargetPath);
							File.Copy(workerNetworkPath, workerTargetPath);
							filesChanged = true;
						}
					}
				}

				// Files changed try to run original binary to update shadow process.
				if (filesChanged)
				{
					IoC.Get<IWorkerAgentService>().KillWorker();
					SingleInstance<App>.Cleanup();
					Process.Start(new ProcessStartInfo
					{
						FileName = originalProcPath,
						Arguments = string.Join(" ", e.Args).Replace(AppArguments.ShadowProc, ""),
						WorkingDirectory = Path.GetDirectoryName(originalProcPath)
					});
					Environment.Exit(0);
				}
			}
			catch (UnauthorizedAccessException)
			{
				// may be already running
			}
			catch (IOException)
			{
				// may be already running
			}
		}

		private static void SpawnShadowProcess(StartupEventArgs e, string assemblyLocation)
		{
			var shadowAssemblyName = $"{Path.GetFileNameWithoutExtension(assemblyLocation)}.shadow.exe";
			var shadowPath = Path.Combine(Path.GetTempPath(), "FBDashboard", shadowAssemblyName);
			try
			{
				if (File.Exists(shadowPath))
				{
					File.Delete(shadowPath);
				}

				Debug.Assert(assemblyLocation != null, "assemblyLocation != null");
				Directory.CreateDirectory(Path.GetDirectoryName(shadowPath));
				File.Copy(assemblyLocation, shadowPath);

				// Copy FBuild folder with worker if exists.
				var workerFolder = Path.Combine(Path.GetDirectoryName(assemblyLocation), "FBuild");
				var workerTargetFolder = Path.Combine(Path.GetDirectoryName(shadowPath), "FBuild");
				if (Directory.Exists(workerFolder))
				{
					Directory.CreateDirectory(workerTargetFolder);
					// Copy all worker files.
					foreach (string newPath in Directory.GetFiles(workerFolder, "*.*", SearchOption.TopDirectoryOnly))
					{
						File.Copy(newPath, newPath.Replace(workerFolder, workerTargetFolder), true);
					}
				}
			}
			catch (UnauthorizedAccessException)
			{
				// may be already running
			}
			catch (IOException)
			{
				// may be already running
			}

			AppBootstrapper.CreateShadowContext(shadowPath);
			SingleInstance<App>.Cleanup();

			var ShadowWorkingDir = Directory.GetCurrentDirectory();
			if (File.Exists(Path.Combine(Path.GetDirectoryName(shadowPath), "FBuild", "FBuildWorker.exe")))
			{
				ShadowWorkingDir = Path.GetDirectoryName(shadowPath);
			}

			Process.Start(new ProcessStartInfo
			{
				FileName = shadowPath,
				Arguments = string.Join(" ", e.Args.Concat(new[] { AppArguments.ShadowProc })),
				WorkingDirectory = ShadowWorkingDir
			});
		}

		protected override void OnExit(object sender, EventArgs e)
		{
			SingleInstance<App>.Cleanup();
			base.OnExit(sender, e);
		}

		protected override object GetInstance(Type serviceType, string key)
			=> _container.GetInstance(serviceType, key);

		protected override IEnumerable<object> GetAllInstances(Type serviceType)
			=> _container.GetAllInstances(serviceType);

		protected override void BuildUp(object instance)
			=> _container.BuildUp(instance);
	}
}
