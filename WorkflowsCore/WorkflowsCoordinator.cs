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
            Func<WorkflowBase, WorkflowBase, Task> onSrcWorkflowCanceled = null)
        {
            var srcWorkflowDefinition = GetWorkflowDefinition(srcWorkflowName);
            var dstWorkflowDefinition = GetWorkflowDefinition(dstWorkflowName);
            var dependency = new ActionDependency(
                srcWorkflowDefinition,
                srcWorkflowAction,
                dstWorkflowDefinition,
                dependencyHandler,
                (src, dst) => onSrcWorkflowCanceled?.Invoke(src, dst) ?? Task.CompletedTask,
                OnUnhandledException);

            _dependencies.Add(dependency);
        }

        public void RegisterWorkflowDependency<TStateSrcWorkflow>(
            TWorkflowName srcWorkflowName,
            TStateSrcWorkflow srcWorkflowState,
            TWorkflowName dstWorkflowName,
            Func<WorkflowBase, WorkflowBase, Task> dependencyHandler,
            Func<WorkflowBase, WorkflowBase, Task> onSrcWorkflowCanceled = null)
        {
            var srcWorkflowDefinition = GetWorkflowDefinition(srcWorkflowName);
            var dstWorkflowDefinition = GetWorkflowDefinition(dstWorkflowName);
            var dependency = new StateDependency<TStateSrcWorkflow>(
                srcWorkflowDefinition,
                srcWorkflowState,
                dstWorkflowDefinition,
                dependencyHandler,
                (src, dst) => onSrcWorkflowCanceled?.Invoke(src, dst) ?? Task.CompletedTask,
                OnUnhandledException);

            _dependencies.Add(dependency);
        }

        public IList<WorkflowBase> GetWorkflows() => 
            _workflows.Values.Select(d => d.Workflow).Where(w => w != null).ToList();

        public void SetWorkflows(
            IWorkflowEngine workflowEngine,
            IEnumerable<KeyValuePair<TWorkflowName, object>> workflows,
            bool initializeDependencies = true)
        {
            foreach (var pair in workflows)
            {
                var workflow = workflowEngine.GetActiveWorkflowById(pair.Value);
                if (workflow != null)
                {
                    AddWorkflow(pair.Key, workflow, initializeDependencies);
                }
            }
        }

        public WorkflowBase GetWorkflow(TWorkflowName workflowName) =>
            GetWorkflowDefinition(workflowName).Workflow;

        public void AddWorkflow(TWorkflowName workflowName, WorkflowBase workflow, bool initializeDependencies = true) =>
            GetWorkflowDefinition(workflowName).SetWorkflow(workflow, initializeDependencies);

        public void CancelWorkflow(TWorkflowName workflowName) =>
            GetWorkflowDefinition(workflowName).CancelWorkflow();

        // ReSharper disable once UnusedParameter.Local
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

            public abstract void OnSrcWorkflowCanceled();

            public abstract void InitializeDependentWorkflow();

            protected void DoWork(Func<Task> handler)
            {
                DoWorkCore(handler).ContinueWith(
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
                    () => DoWork(
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

            public override void OnSrcWorkflowCanceled()
            {
                _cancellationTokenSource.Cancel();
                DoWork(
                    async () =>
                    {
                        await OnSrcWorkflowCanceledHandler(
                            SrcWorkflowDefinition.Workflow,
                            DstWorkflowDefinition.Workflow);
                    });
            }

            public override void InitializeDependentWorkflow()
            {
                DoWork(
                    async () =>
                    {
                        if (SrcWorkflowDefinition.Workflow == null)
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
                    });
            }
        }

        private class StateDependency<TState> : Dependency
        {
            private readonly TState _srcWorkflowState;
            private CancellationTokenSource _cancellationTokenSource;

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
                _cancellationTokenSource = new CancellationTokenSource();
                Utilities.SetCurrentCancellationTokenTemporarily(
                    _cancellationTokenSource.Token,
                    () => DoWork(
                        async () =>
                        {
                            var checkInitialState = true;
                            while (true)
                            {
                                await ((WorkflowBase<TState>)SrcWorkflowDefinition.Workflow).WaitForState(
                                    _srcWorkflowState, checkInitialState);
                                checkInitialState = false;
                                if (DstWorkflowDefinition.Workflow == null)
                                {
                                    continue;
                                }

                                await DependencyHandler(SrcWorkflowDefinition.Workflow, DstWorkflowDefinition.Workflow);
                            }

                            // ReSharper disable once FunctionNeverReturns
                        }));
            }

            public override void OnSrcWorkflowCanceled()
            {
                _cancellationTokenSource.Cancel();
                DoWork(
                    async () =>
                    {
                        await OnSrcWorkflowCanceledHandler(
                            SrcWorkflowDefinition.Workflow,
                            DstWorkflowDefinition.Workflow);
                    });
            }

            public override void InitializeDependentWorkflow()
            {
                DoWork(
                    async () =>
                    {
                        if (SrcWorkflowDefinition.Workflow == null)
                        {
                            return;
                        }

                        var wasIn = await SrcWorkflowDefinition.Workflow.RunViaWorkflowTaskScheduler(
                            () => ((WorkflowBase<TState>)SrcWorkflowDefinition.Workflow).WasIn(
                                _srcWorkflowState,
                                ignoreSuppression: true));

                        if (!wasIn)
                        {
                            return;
                        }

                        await DependencyHandler(SrcWorkflowDefinition.Workflow, DstWorkflowDefinition.Workflow);
                    });
            }
        }

        private class WorkflowDefinition
        {
            public WorkflowBase Workflow { get; private set; }

            public IList<Dependency> OutgoingDependencies { get; } = new List<Dependency>();

            public IList<Dependency> IngoingDependencies { get; } = new List<Dependency>();

            public void SetWorkflow(WorkflowBase workflow, bool initializeDependencies)
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
                        dependency.InitializeDependentWorkflow();
                    }
                }

                foreach (var dependency in OutgoingDependencies)
                {
                    dependency.OnSrcWorkflowSet();
                }
            }

            public void CancelWorkflow()
            {
                if (Workflow == null)
                {
                    return;
                }

                foreach (var dependency in OutgoingDependencies)
                {
                    dependency.OnSrcWorkflowCanceled();
                }

                Workflow.CancelWorkflow();
                Workflow = null;
            }
        }
    }
}
