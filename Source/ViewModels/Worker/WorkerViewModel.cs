using System;
using System.Linq;
using System.Timers;
using System.IO;
using System.Net;
using Caliburn.Micro;
using FastBuild.Dashboard.Services;
using FastBuild.Dashboard.Services.Worker;

namespace FastBuild.Dashboard.ViewModels.Worker
{
	internal class WorkerViewModel : PropertyChangedBase, IMainPage
	{
		private readonly IWorkerAgentService _workerAgentService;
		private string _workerErrorMessage;
		private string _statusTitle;
		public string DisplayName => "Worker";

		public bool IsWorkerRunning => _workerAgentService.IsRunning;

		public string Icon => "Worker";

		public string WorkerErrorMessage
		{
			get => _workerErrorMessage;
			private set
			{
				if (value == _workerErrorMessage)
				{
					return;
				}

				_workerErrorMessage = value;
				this.NotifyOfPropertyChange();
			}
		}

		public string StatusTitle
		{
			get => _statusTitle;
			private set
			{
				if (value == _statusTitle)
				{
					return;
				}

				_statusTitle = value;
				this.NotifyOfPropertyChange();
			}
		}

		public BindableCollection<WorkerCoreStatusViewModel> CoreStatuses { get; }
			= new BindableCollection<WorkerCoreStatusViewModel>();

		private bool _isTicking;

		public event EventHandler<WorkerMode> WorkerModeChanged;

		public WorkerViewModel()
		{
			this.StatusTitle = "Preparing...";

			_workerAgentService = IoC.Get<IWorkerAgentService>();
			_workerAgentService.WorkerRunStateChanged += this.WorkerAgentService_WorkerRunStateChanged;
			_workerAgentService.Initialize();

			var tickTimer = new Timer(500)
			{
				AutoReset = true
			};
			tickTimer.Elapsed += this.Tick;
			tickTimer.Start();
		}

		private void Tick(object sender, ElapsedEventArgs e)
		{
			if (!this.IsWorkerRunning)
			{
				return;
			}

			if (_isTicking)
			{
				return; 
			}

			_isTicking = true;

			var statuses = _workerAgentService.GetStatus();

			for (var i = this.CoreStatuses.Count - 1; i > statuses.Length; --i)
			{
				this.CoreStatuses.RemoveAt(i);
			}

			for (var i = this.CoreStatuses.Count; i < statuses.Length; ++i)
			{
				this.CoreStatuses.Add(new WorkerCoreStatusViewModel(i));
			}

			for (var i = 0; i < this.CoreStatuses.Count; ++i)
			{
				this.CoreStatuses[i].UpdateStatus(statuses[i]);
			}

			if (statuses.All(s => s.State == WorkerCoreState.Disabled))
			{
				this.StatusTitle = "Disabled";
			}
			else if (statuses.Any(s => s.State == WorkerCoreState.Working))
			{
				this.StatusTitle = "Working";
			}
			else
			{
				this.StatusTitle = "Idle";
			}

			CheckWorkerBlacklist();

			_isTicking = false;
		}

		private void CheckWorkerBlacklist()
		{
			var brokeragePath = IoC.Get<IBrokerageService>().BrokeragePath;
			var blacklistFile = Path.Combine(brokeragePath, "blacklist.txt");
			var whitelistFile = Path.Combine(brokeragePath, "whitelist.txt");

			try
			{
				var hostName = Dns.GetHostName();

				if (File.Exists(blacklistFile))
				{
					string[] blacklist = File.ReadAllLines(blacklistFile);
					foreach (string s in blacklist)
					{
						if (s == hostName)
						{
							if (_workerAgentService.WorkerMode != WorkerMode.Disabled)
							{
								_workerAgentService.WorkerMode = WorkerMode.Disabled;
								this.WorkerModeChanged?.Invoke(this, _workerAgentService.WorkerMode);
							}
							return;
						}
					}
				}

				if (File.Exists(whitelistFile))
				{
					string[] whitelist = File.ReadAllLines(whitelistFile);
					foreach (string s in whitelist)
					{
						if (s == hostName)
						{
							if (_workerAgentService.WorkerMode != WorkerMode.WorkWhenIdle)
							{
								_workerAgentService.WorkerMode = WorkerMode.WorkWhenIdle;
								this.WorkerModeChanged?.Invoke(this, _workerAgentService.WorkerMode);
							}
							return;
						}
					}
				}
			}
			catch (Exception)
			{
				// Don't worry about it. Not critical.
			}
		}

		private void WorkerAgentService_WorkerRunStateChanged(object sender, WorkerRunStateChangedEventArgs e)
		{
			this.NotifyOfPropertyChange(nameof(this.IsWorkerRunning));
			this.WorkerErrorMessage = e.ErrorMessage;

			if (!e.IsRunning)
			{
				this.StatusTitle = "Worker Error";
			}
		}
	}
}
