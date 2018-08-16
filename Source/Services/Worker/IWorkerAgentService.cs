using System;

namespace FastBuild.Dashboard.Services.Worker
{
	internal interface IWorkerAgentService
	{
		int WorkerCores { get; set; }
		WorkerMode WorkerMode { get; set; }
		bool IsRunning { get; }
		event EventHandler<WorkerRunStateChangedEventArgs> WorkerRunStateChanged;
		void Initialize();
		void KillWorker();
		WorkerCoreStatus[] GetStatus();
	}
}
