using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;

namespace WorkflowsCore
{
    public enum WorkflowStatus
    {
        InProgress = 0,
        Completed = 1,
        Canceled = 2,
        Failed = 3,
        Sleeping = 4
    }

    public interface IWorkflowData
    {
        IReadOnlyDictionary<string, object> Data { get; }

        IReadOnlyDictionary<string, object> TransientData { get; }

        T GetData<T>(string key);

        void SetData<T>(string key, T value);

        void SetData(IReadOnlyDictionary<string, object> newData);

        T GetTransientData<T>(string key);

        void SetTransientData<T>(string key, T value);

        void SetTransientData(IReadOnlyDictionary<string, object> newData);
    }

    public interface IWorkflowStateRepository
    {
        void SaveWorkflowData(WorkflowBase workflow);

        void MarkWorkflowAsCompleted(WorkflowBase workflow);

        void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception);

        void MarkWorkflowAsCanceled(WorkflowBase workflow);

        void MarkWorkflowAsSleeping(WorkflowBase workflow);

        void MarkWorkflowAsInProgress(WorkflowBase workflow);
    }

    public interface IWorkflowRepository
    {
        /// <summary>
        /// It should return workflows in progress and faulted. It should not return sleeping workflows.
        /// </summary>
        IList<WorkflowInstance> GetActiveWorkflows();

        WorkflowInstance GetSleepingOrFaultedWorkflowById(object workflowId);

        WorkflowStatus GetWorkflowStatusById(object workflowId);
    }

    public interface IDependencyInjectionContainer
    {
        object Resolve(Type type);
    }

    public interface IWorkflowEngine
    {
        Task LoadingTask { get; }

        IList<WorkflowBase> RunningWorkflows { get; }

        WorkflowBase CreateWorkflow(string fullTypeName);

        WorkflowBase CreateWorkflow(string fullTypeName, IReadOnlyDictionary<string, object> initialWorkflowData);

        Task LoadAndExecuteActiveWorkflowsAsync();

        WorkflowBase GetActiveWorkflowById(object id);

        Task GetWorkflowCompletedTaskById(object workflowId);
    }

    public static class Utilities
    {
        private static readonly AsyncLocal<CancellationToken> AsyncCancellationToken = new AsyncLocal<CancellationToken>();
        private static readonly ITimeProvider DefaultTimeProvider = new TimeProvider();
        private static readonly AsyncLocal<ITimeProvider> AsyncTimeProvider = new AsyncLocal<ITimeProvider>();
        private static readonly AsyncLocal<TaskScheduler> AsyncTaskScheduler = new AsyncLocal<TaskScheduler>();

        public static CancellationToken CurrentCancellationToken => AsyncCancellationToken.Value;

        public static TaskScheduler WorkflowsTaskScheduler
        {
            get { return AsyncTaskScheduler.Value; }
            set { AsyncTaskScheduler.Value = value; }
        }

        public static ITimeProvider TimeProvider
        {
            get
            {
                return AsyncTimeProvider.Value ?? DefaultTimeProvider;
            }

            set
            {
                AsyncTimeProvider.Value = value;
            }
        }

        public static void SetCurrentCancellationTokenTemporarily(CancellationToken ct, Action action)
        {
            var oldValue = AsyncCancellationToken.Value;
            AsyncCancellationToken.Value = ct;
            try
            {
                action();
            }
            finally
            {
                AsyncCancellationToken.Value = oldValue;
            }
        }

        public static T SetCurrentCancellationTokenTemporarily<T>(CancellationToken ct, Func<T> func)
        {
            var oldValue = AsyncCancellationToken.Value;
            AsyncCancellationToken.Value = ct;
            try
            {
                return func();
            }
            finally
            {
                AsyncCancellationToken.Value = oldValue;
            }
        }

        public static Task RunViaWorkflowsTaskScheduler(Action action)
        {
            if (WorkflowsTaskScheduler == null)
            {
                action();
                return Task.CompletedTask;
            }

            return Task.Factory.StartNew(
                action,
                CancellationToken.None,
                TaskCreationOptions.None,
                WorkflowsTaskScheduler);
        }

        public static Task<T> RunViaWorkflowsTaskScheduler<T>(Func<T> func)
        {
            if (WorkflowsTaskScheduler == null)
            {
                return Task.FromResult(func());
            }

            return Task.Factory.StartNew(
                func,
                CancellationToken.None,
                TaskCreationOptions.None,
                WorkflowsTaskScheduler);
        }
    }

    public class StateStats
    {
        public StateStats(int counter = 0)
        {
            EnteredCounter = counter;
            IgnoreSuppressionEnteredCounter = counter;
        }

        public int EnteredCounter { get; set; }

        public int IgnoreSuppressionEnteredCounter { get; set; }
    }
}
