using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Timers;

namespace FastBuild.Dashboard.Services
{
	internal class BrokerageService : IBrokerageService
	{
		private const string WorkerPoolRelativePath = "main";

		private string _workerPoolPath;

		private string[] _workerNames;

		public string[] WorkerNames
		{
			get => _workerNames;
			private set
			{
				var oldCount = _workerNames.Length;
				_workerNames = value;

				if (oldCount != _workerNames.Length)
				{
					this.WorkerCountChanged?.Invoke(this, EventArgs.Empty);
				}
			}
		}

		private bool _isUpdatingWorkers;

		public string BrokeragePath
		{
			get => Environment.GetEnvironmentVariable("FASTBUILD_BROKERAGE_PATH");
			set => Environment.SetEnvironmentVariable("FASTBUILD_BROKERAGE_PATH", value);
		}

		public event EventHandler WorkerCountChanged;

		public BrokerageService()
		{
			_workerNames = new string[0];

			var checkTimer = new Timer(5000);
			checkTimer.Elapsed += this.CheckTimer_Elapsed;
			checkTimer.AutoReset = true;
			checkTimer.Enabled = true;
			this.UpdateWorkers();
		}

		private void CheckTimer_Elapsed(object sender, ElapsedEventArgs e) => this.UpdateWorkers();

		private void UpdateWorkers()
		{
			if (_isUpdatingWorkers)
				return;

			_isUpdatingWorkers = true;

			try
			{
				var brokeragePath = this.BrokeragePath;
				if (string.IsNullOrEmpty(brokeragePath))
				{
					this.WorkerNames = new string[0];
					return;
				}

				try
				{
					if (string.IsNullOrEmpty(this._workerPoolPath))
					{
						this._workerPoolPath = FindWorkerPoolPath();
					}

					this.WorkerNames = Directory.GetFiles(this._workerPoolPath)
						.Select(Path.GetFileName)
						.TakeWhile(worker => IsWorkerActive(Path.Combine(this._workerPoolPath, worker)))
						.ToArray();
				}
				catch (IOException)
				{
					this.WorkerNames = new string[0];
				}
			}
			finally
			{
				_isUpdatingWorkers = false;
			}
		}

		private string FindWorkerPoolPath()
		{
			try
			{
				var hostName = Dns.GetHostName();
				var workerPoolPath = Path.Combine(this.BrokeragePath, WorkerPoolRelativePath);

				// Search for the most recent protocol version used by this machine.
				return Directory.GetDirectories(workerPoolPath, "*.windows")
					.OrderByDescending(Path.GetFileName, Comparer<string>.Create((pool1, pool2) =>
					{
						// Ensure correct numeric sorting, e.g. 12 comes after 2.
						int version1, version2;
						if (Int32.TryParse(pool1.Substring(0, pool1.IndexOf('.')), out version1) &&
							Int32.TryParse(pool2.Substring(0, pool2.IndexOf('.')), out version2))
						{
							return version1.CompareTo(version2);
						}

						return 0;
					}))
					.First(versionPath =>
					{
						var hostFilePath = Path.Combine(workerPoolPath, versionPath, hostName);
						return File.Exists(hostFilePath) && IsWorkerActive(hostFilePath);
					});
			}
			catch (Exception)
			{
				throw new IOException("Unable to find the worker pool of the current host");
			}
		}

		private bool IsWorkerActive(string workerFilePath)
		{
			return File.GetLastWriteTimeUtc(workerFilePath).AddMinutes(2.0) >= DateTime.UtcNow;
		}
	}
}
