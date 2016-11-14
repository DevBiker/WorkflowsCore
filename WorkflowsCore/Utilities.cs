using System;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;

namespace WorkflowsCore
{
    public static class Utilities
    {
        private static readonly AsyncLocal<CancellationToken> AsyncCancellationToken = new AsyncLocal<CancellationToken>();
        private static readonly ITimeProvider DefaultTimeProvider = new TimeProvider();
        private static readonly AsyncLocal<ITimeProvider> AsyncTimeProvider = new AsyncLocal<ITimeProvider>();
        private static readonly AsyncLocal<TaskScheduler> AsyncTaskScheduler = new AsyncLocal<TaskScheduler>();
        private static readonly IWorkflowMetadataCache DefaultWorkflowMetadataCache = new WorkflowMetadataCache();
        private static IWorkflowMetadataCache _workflowMetadataCache = DefaultWorkflowMetadataCache;

        public static IWorkflowMetadataCache WorkflowMetadataCache
        {
            get
            {
                return _workflowMetadataCache;
            }

            set
            {
                var oldValue = Interlocked.CompareExchange(
                    ref _workflowMetadataCache,
                    value,
                    DefaultWorkflowMetadataCache);
                if (ReferenceEquals(_workflowMetadataCache, oldValue))
                {
                    throw new InvalidOperationException();
                }
            }
        }

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
}