using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WorkflowsCore
{
    public class UnhandledExceptionEventArgs : EventArgs
    {
        public UnhandledExceptionEventArgs(Exception exception)
        {
            Exception = exception;
        }

        public Exception Exception { get; }
    }

    public class WorkflowsCoordinator<TWorkflowName>
    {
        private readonly IList<Dependency> _dependencies = new List<Dependency>(); 
        private readonly IDictionary<TWorkflowName, WorkflowDefinition> _workflows =
            new Dictionary<TWorkflowName, WorkflowDefinition>();

        public event EventHandler<UnhandledExceptionEventArgs> UnhandledException;

        public void RegisterWorkflowDependency(
            TWorkflowName srcWorkflowName,
            string srcWorkflowAction,
            TWorkflowName dstWorkflowName,
            Func<WorkflowBase, WorkflowBase, Task> dependencyHandler,
            Func<WorkflowBase, WorkflowBase, Task> onSrcWorkflowCanceled = null,
            string onSrcWorkflowCanceledClearTimesExecutedForAction = null)
        {
            onSrcWorkflowCanceled = ProcessCancelHandlers(
                onSrcWorkflowCanceled,
                onSrcWorkflowCanceledClearTimesExecutedForAction);

            var srcWorkflowDefinition = GetWorkflowDefinition(srcWorkflowName);
            var dstWorkflowDefinition = GetWorkflowDefinition(dstWorkflowName);
            var dependency = new ActionDependency(
                srcWorkflowDefinition,
                srcWorkflowAction,
                dstWorkflowDefinition,
                dependencyHandler,
                onSrcWorkflowCanceled,
                OnUnhandledException);

            _dependencies.Add(dependency);
        }

        public void RegisterWorkflowDependency<TStateSrcWorkflow>(
            TWorkflowName srcWorkflowName,
            TStateSrcWorkflow srcWorkflowState,
            TWorkflowName dstWorkflowName,
            Func<WorkflowBase, WorkflowBase, Task> dependencyHandler,
            Func<WorkflowBase, WorkflowBase, Task> onSrcWorkflowCanceled = null,
            string onSrcWorkflowCanceledClearTimesExecutedForAction = null)
        {
            onSrcWorkflowCanceled = ProcessCancelHandlers(
                onSrcWorkflowCanceled,
                onSrcWorkflowCanceledClearTimesExecutedForAction);

            var srcWorkflowDefinition = GetWorkflowDefinition(srcWorkflowName);
            var dstWorkflowDefinition = GetWorkflowDefinition(dstWorkflowName);
            var dependency = new StateDependency<TStateSrcWorkflow>(
                srcWorkflowDefinition,
                srcWorkflowState,
                dstWorkflowDefinition,
                dependencyHandler,
                onSrcWorkflowCanceled,
                OnUnhandledException);

            _dependencies.Add(dependency);
        }

        public IList<WorkflowBase> GetWorkflows() => 
            _workflows.Values.Select(d => d.Workflow).Where(w => w != null).ToList();

        public async Task SetWorkflowsAsync(
            IWorkflowEngine workflowEngine,
            IEnumerable<KeyValuePair<TWorkflowName, object>> workflows,
            bool initializeDependencies = true)
        {
            foreach (var pair in workflows)
            {
                var workflow = workflowEngine.GetActiveWorkflowById(pair.Value);
                if (workflow != null)
                {
                    await AddWorkflowAsync(pair.Key, workflow, initializeDependencies);
                }
            }
        }

        public WorkflowBase GetWorkflow(TWorkflowName workflowName) =>
            GetWorkflowDefinition(workflowName).Workflow;

        public Task AddWorkflowAsync(
            TWorkflowName workflowName,
            WorkflowBase workflow,
            bool initializeDependencies = true)
        {
            return GetWorkflowDefinition(workflowName).SetWorkflowAsync(workflow, initializeDependencies);
        }

        public Task CancelWorkflowAsync(TWorkflowName workflowName) =>
            GetWorkflowDefinition(workflowName).CancelWorkflowAsync();

        private static Func<WorkflowBase, WorkflowBase, Task> ProcessCancelHandlers(
            Func<WorkflowBase, WorkflowBase, Task> onSrcWorkflowCanceled,
            string onSrcWorkflowCanceledClearTimesExecutedForAction)
        {
            return async (src, dst) =>
            {
                if (onSrcWorkflowCanceledClearTimesExecutedForAction != null)
                {
                    await dst.ClearTimesExecutedAsync(onSrcWorkflowCanceledClearTimesExecutedForAction);
                }

                if (onSrcWorkflowCanceled != null)
                {
                    await onSrcWorkflowCanceled(src, dst);
                }
            };
        }

        private WorkflowDefinition GetWorkflowDefinition(TWorkflowName workflowName)
        {
            WorkflowDefinition definition;
            if (_workflows.TryGetValue(workflowName, out definition))
            {
                return definition;
            }

            definition = new WorkflowDefinition();
            _workflows.Add(workflowName, definition);

            return definition;
        }

        private void OnUnhandledException(Exception ex) => 
            UnhandledException?.Invoke(this, new UnhandledExceptionEventArgs(ex));

        private abstract class Dependency
        {
            private readonly Action<Exception> _onUnhandledException;

            protected Dependency(
                WorkflowDefinition srcWorkflowDefinition,
                WorkflowDefinition dstWorkflowDefinition,
                Func<WorkflowBase, WorkflowBase, Task> dependencyHandler,
                Func<WorkflowBase, WorkflowBase, Task> onSrcWorkflowCanceledHandler,
                Action<Exception> onUnhandledException)
            {
                _onUnhandledException = onUnhandledException;
                if (srcWorkflowDefinition.Workflow != null || dstWorkflowDefinition.Workflow != null)
                {
                    throw new InvalidOperationException();
                }

                SrcWorkflowDefinition = srcWorkflowDefinition;
                DstWorkflowDefinition = dstWorkflowDefinition;
                DependencyHandler = dependencyHandler;
                OnSrcWorkflowCanceledHandler = onSrcWorkflowCanceledHandler;

                SrcWorkflowDefinition.OutgoingDependencies.Add(this);
                DstWorkflowDefinition.IngoingDependencies.Add(this);
            }

            protected WorkflowDefinition SrcWorkflowDefinition { get; }

            protected WorkflowDefinition DstWorkflowDefinition { get; }

            protected Func<WorkflowBase, WorkflowBase, Task> DependencyHandler { get; }

            protected Func<WorkflowBase, WorkflowBase, Task> OnSrcWorkflowCanceledHandler { get; }

            public abstract void OnSrcWorkflowSet();

            public abstract Task OnSrcWorkflowCanceledAsync();

            public abstract Task InitializeDependentWorkflowAsync();

            protected Task DoWork(Func<Task> handler)
            {
                return DoWorkCore(handler).ContinueWith(
                    t =>
                    {
                        if (t.Result != null)
                        {
                            _onUnhandledException(t.Result);
                        }
                    });
            }

            private static async Task<Exception> DoWorkCore(Func<Task> handler)
            {
                try
                {
                    await handler();
                }
                catch (TaskCanceledException)
                {
                }
                catch (Exception ex)
                {
                    return ex;
                }

                return null;
            }
        }

        private class ActionDependency : Dependency
        {
            private readonly string _srcWorkflowAction;
            private CancellationTokenSource _cancellationTokenSource;
            private Task _observerTask;

            public ActionDependency(
                WorkflowDefinition srcWorkflowDefinition,
                string srcWorkflowAction,
                WorkflowDefinition dstWorkflowDefinition,
                Func<WorkflowBase, WorkflowBase, Task> dependencyHandler,
                Func<WorkflowBase, WorkflowBase, Task> onSrcWorkflowCanceled,
                Action<Exception> onUnhandledException)
                : base(
                    srcWorkflowDefinition,
                    dstWorkflowDefinition,
                    dependencyHandler,
                    onSrcWorkflowCanceled,
                    onUnhandledException)
            {
                _srcWorkflowAction = srcWorkflowAction;
            }

            public override void OnSrcWorkflowSet()
            {
                _cancellationTokenSource =
                    CancellationTokenSource.CreateLinkedTokenSource(Utilities.CurrentCancellationToken);
                Utilities.SetCurrentCancellationTokenTemporarily(
                    _cancellationTokenSource.Token,
                    () => _observerTask = DoWork(
                        async () =>
                        {
                            while (true)
                            {
                                await SrcWorkflowDefinition.Workflow.WaitForAction(_srcWorkflowAction);
                                if (DstWorkflowDefinition.Workflow == null)
                                {
                                    continue;
                                }

                                await DependencyHandler(SrcWorkflowDefinition.Workflow, DstWorkflowDefinition.Workflow);
                            }

                            // ReSharper disable once FunctionNeverReturns
                        }));
            }

            public override async Task OnSrcWorkflowCanceledAsync()
            {
                var cts = _cancellationTokenSource;
                if (cts == null)
                {
                    return; // OnSrcWorkflowSet() was not executed yet, nothing to cancel or cancellation was started already
                }

                _cancellationTokenSource = null;
                cts.Cancel();
                await _observerTask;
                cts.Dispose();

                if (DstWorkflowDefinition.Workflow == null)
                {
                    return;
                }

                await OnSrcWorkflowCanceledHandler(SrcWorkflowDefinition.Workflow, DstWorkflowDefinition.Workflow);
            }

            public override async Task InitializeDependentWorkflowAsync()
            {
                if (SrcWorkflowDefinition.Workflow == null || DstWorkflowDefinition.Workflow == null)
                {
                    return;
                }

                var wasExecuted = await SrcWorkflowDefinition.Workflow.RunViaWorkflowTaskScheduler(
                    () => SrcWorkflowDefinition.Workflow.WasExecuted(_srcWorkflowAction));

                if (!wasExecuted)
                {
                    return;
                }

                await DependencyHandler(SrcWorkflowDefinition.Workflow, DstWorkflowDefinition.Workflow);
            }
        }

        private class StateDependency<TState> : Dependency
        {
            private readonly TState _srcWorkflowState;
            private CancellationTokenSource _cancellationTokenSource;
            private Task _observerTask;

            public StateDependency(
                WorkflowDefinition srcWorkflowDefinition,
                TState srcWorkflowState,
                WorkflowDefinition dstWorkflowDefinition,
                Func<WorkflowBase, WorkflowBase, Task> dependencyHandler,
                Func<WorkflowBase, WorkflowBase, Task> onSrcWorkflowCanceled,
                Action<Exception> onUnhandledException)
                : base(
                    srcWorkflowDefinition,
                    dstWorkflowDefinition,
                    dependencyHandler,
                    onSrcWorkflowCanceled,
                    onUnhandledException)
            {
                _srcWorkflowState = srcWorkflowState;
            }

            public override void OnSrcWorkflowSet()
            {
                _cancellationTokenSource =
                    CancellationTokenSource.CreateLinkedTokenSource(Utilities.CurrentCancellationToken);
                Utilities.SetCurrentCancellationTokenTemporarily(
                    _cancellationTokenSource.Token,
                    () => _observerTask = DoWork(
                        async () =>
                        {
                            while (true)
                            {
                                await ((WorkflowBase<TState>)SrcWorkflowDefinition.Workflow).WaitForState(
                                    _srcWorkflowState, checkInitialState: false);
                                if (DstWorkflowDefinition.Workflow == null)
                                {
                                    continue;
                                }

                                await DependencyHandler(SrcWorkflowDefinition.Workflow, DstWorkflowDefinition.Workflow);
                            }

                            // ReSharper disable once FunctionNeverReturns
                        }));
            }

            public override async Task OnSrcWorkflowCanceledAsync()
            {
                var cts = _cancellationTokenSource;
                if (cts == null)
                {
                    return; // OnSrcWorkflowSet() was not executed yet, nothing to cancel or cancellation was started already
                }

                _cancellationTokenSource = null;
                cts.Cancel();
                await _observerTask;
                cts.Dispose();

                if (DstWorkflowDefinition.Workflow == null)
                {
                    return;
                }

                await OnSrcWorkflowCanceledHandler(SrcWorkflowDefinition.Workflow, DstWorkflowDefinition.Workflow);
            }

            public override async Task InitializeDependentWorkflowAsync()
            {
                if (SrcWorkflowDefinition.Workflow == null || DstWorkflowDefinition.Workflow == null)
                {
                    return;
                }

                var wasIn = await SrcWorkflowDefinition.Workflow.RunViaWorkflowTaskScheduler(
                    () => ((WorkflowBase<TState>)SrcWorkflowDefinition.Workflow).WasIn(_srcWorkflowState));

                if (!wasIn)
                {
                    return;
                }

                await DependencyHandler(SrcWorkflowDefinition.Workflow, DstWorkflowDefinition.Workflow);
            }
        }

        private class WorkflowDefinition
        {
            public WorkflowBase Workflow { get; private set; }

            public IList<Dependency> OutgoingDependencies { get; } = new List<Dependency>();

            public IList<Dependency> IngoingDependencies { get; } = new List<Dependency>();

            public async Task SetWorkflowAsync(WorkflowBase workflow, bool initializeDependencies)
            {
                if (workflow == null)
                {
                    throw new ArgumentNullException(nameof(workflow));
                }

                if (Workflow != null)
                {
                    throw new InvalidOperationException();
                }

                Workflow = workflow;
                if (initializeDependencies)
                {
                    foreach (var dependency in IngoingDependencies)
                    {
                        await dependency.InitializeDependentWorkflowAsync();
                    }
                }

                foreach (var dependency in OutgoingDependencies)
                {
                    if (initializeDependencies)
                    {
                        await dependency.InitializeDependentWorkflowAsync();
                    }

                    if (Workflow == null)
                    {
                        return;  // Workflow was canceled
                    }

                    dependency.OnSrcWorkflowSet();
                }
            }

            public async Task CancelWorkflowAsync()
            {
                if (Workflow == null)
                {
                    return;
                }

                foreach (var dependency in OutgoingDependencies)
                {
                    await dependency.OnSrcWorkflowCanceledAsync();
                }

                Workflow.CancelWorkflow();
                Workflow = null;
            }
        }
    }
}
