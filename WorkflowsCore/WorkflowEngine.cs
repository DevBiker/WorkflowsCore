using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkflowsCore.Time;

namespace WorkflowsCore
{
    public sealed class WorkflowEngine : IWorkflowEngine, IDisposable
    {
        private readonly IDependencyInjectionContainer _diContainer;
        private readonly Func<IWorkflowRepository> _workflowRepoFactory;
        private readonly HashSet<WorkflowBase> _runningWorkflows = new HashSet<WorkflowBase>();
        private readonly Dictionary<object, TaskCompletionSource<WorkflowBase>> _workflowsById = new Dictionary<object, TaskCompletionSource<WorkflowBase>>();
        private readonly PreloadWorkflow _preloadWorkflow = new PreloadWorkflow();
        private bool _loadingStarted;

        public WorkflowEngine(IDependencyInjectionContainer diContainer, Func<IWorkflowRepository> workflowRepoFactory)
        {
            _diContainer = diContainer;
            _workflowRepoFactory = workflowRepoFactory;
        }

        public IList<WorkflowBase> RunningWorkflows
        {
            get
            {
                lock (_runningWorkflows)
                {
                    return _runningWorkflows.ToList();
                }
            }
        }

        public Task PreloadWorkflowsTask => _preloadWorkflow.CompletedTask;

        public WorkflowBase CreateWorkflow(string fullTypeName) => CreateWorkflow(fullTypeName, null);

        public WorkflowBase CreateWorkflow(
            string fullTypeName,
            IReadOnlyDictionary<string, object> initialWorkflowData)
        {
            return CreateWorkflowCore(fullTypeName, initialWorkflowData: initialWorkflowData);
        }

        public WorkflowBase CreateWorkflow(
            string fullTypeName,
            IReadOnlyDictionary<string, object> initialWorkflowData,
            IReadOnlyDictionary<string, object> initialWorkflowTransientData)
        {
            return CreateWorkflowCore(
                fullTypeName,
                initialWorkflowData: initialWorkflowData,
                initialWorkflowTransientData: initialWorkflowTransientData);
        }

        public Task LoadAndExecuteActiveWorkflowsAsync() => LoadAndExecuteActiveWorkflowsAsync(6);

        public async Task LoadAndExecuteActiveWorkflowsAsync(int preloadHours)
        {
            await PreloadWorkflows(true, preloadHours);

            _preloadWorkflow.StartWorkflow(
                initialWorkflowTransientData:
                    new Dictionary<string, object>
                    {
                        [nameof(PreloadWorkflow.PreloadHours)] = preloadHours,
                        [nameof(PreloadWorkflow.WorkflowEngine)] = this
                    });

            await _preloadWorkflow.StartedTask;
        }

        public Task<WorkflowBase> GetActiveWorkflowByIdAsync(object id)
        {
            id = id ?? throw new ArgumentNullException(nameof(id));
            lock (_workflowsById)
            {
                TaskCompletionSource<WorkflowBase> workflowTcs;
                if (_workflowsById.TryGetValue(id, out workflowTcs))
                {
                    return workflowTcs.Task;
                }

                workflowTcs = new TaskCompletionSource<WorkflowBase>(TaskContinuationOptions.RunContinuationsAsynchronously);
                _workflowsById.Add(id, workflowTcs);

                CreateWorkflowRepositoryAndDoAction(r => r.GetActiveWorkflowByIdAsync(id))
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            workflowTcs.SetException(t.Exception.GetBaseException());
                            return;
                        }

                        var instance = t.Result;
                        if (instance == null)
                        {
                            workflowTcs.SetResult(null);
                        }

                        var workflow = CreateWorkflowCore(
                            instance.WorkflowTypeName,
                            loadedWorkflowData: instance.Data,
                            id: instance.Id,
                            loadingOnDemand: true);
                        workflowTcs.SetResult(workflow);
                    });

                return workflowTcs.Task;
            }
        }

        void IDisposable.Dispose() => _preloadWorkflow.CancelWorkflow();

        private WorkflowBase CreateWorkflowCore(
            string fullTypeName,
            IReadOnlyDictionary<string, object> initialWorkflowData = null,
            IReadOnlyDictionary<string, object> loadedWorkflowData = null,
            IReadOnlyDictionary<string, object> initialWorkflowTransientData = null,
            object id = null,
            bool loadingOnDemand = false)
        {
            if (initialWorkflowData != null && loadedWorkflowData != null)
            {
                throw new ArgumentOutOfRangeException();
            }

            var workflow = (WorkflowBase)_diContainer.Resolve(Utilities.GetType(fullTypeName));

            workflow.StartWorkflow(
                id,
                initialWorkflowData,
                loadedWorkflowData,
                initialWorkflowTransientData: initialWorkflowTransientData,
                beforeWorkflowStarted: () =>
                {
                    if (!loadingOnDemand && workflow.Id != null)
                    {
                        lock (_workflowsById)
                        {
                            if (workflow.Metadata.GetTransientDataField<DateTime?>(workflow, "NextActivationDate") !=
                                null)
                            {
                                throw new InvalidOperationException(
                                    "Workflow should be saved first time with NextActivationDate as null");
                            }

                            _workflowsById.Add(workflow.Id, CreateTaskCompletionSourceFromWorkflow(workflow));
                        }
                    }
                },
                afterWorkflowFinished: () =>
                {
                    lock (_runningWorkflows)
                    {
                        _runningWorkflows.Remove(workflow);
                    }

                    if (workflow.Id != null)
                    {
                        lock (_workflowsById)
                        {
                            _workflowsById.Remove(workflow.Id);
                        }
                    }
                });
            lock (_runningWorkflows)
            {
                _runningWorkflows.Add(workflow);
            }

            return workflow;
        }

        private TaskCompletionSource<WorkflowBase> CreateTaskCompletionSourceFromWorkflow(WorkflowBase workflow)
        {
            var tcs = new TaskCompletionSource<WorkflowBase>(TaskContinuationOptions.RunContinuationsAsynchronously);
            tcs.SetResult(workflow);
            return tcs;
        }

        private Task PreloadWorkflows(bool checkLoadingStarted, int preloadHours)
        {
            lock (_workflowsById)
            {
                if (checkLoadingStarted)
                {
                    if (_loadingStarted)
                    {
                        throw new InvalidOperationException();
                    }

                    _loadingStarted = true;
                }

                return CreateWorkflowRepositoryAndDoAction(
                    r => r.GetActiveWorkflowsAsync(
                        Utilities.SystemClock.Now.AddHours(preloadHours),
                        RunningWorkflows.Where(w => w.Id != null).Select(w => w.Id)))
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            _preloadWorkflow.StopWorkflow(t.Exception.GetBaseException());
                            return;
                        }

                        var workflowInstances = t.Result;

                        lock (_workflowsById)
                        {
                            foreach (var i in workflowInstances)
                            {
                                if (_workflowsById.ContainsKey(i.Id))
                                {
                                    continue;
                                }

                                var workflow = CreateWorkflowCore(
                                    i.WorkflowTypeName,
                                    loadedWorkflowData: i.Data,
                                    id: i.Id,
                                    loadingOnDemand: true);

                                _workflowsById.Add(i.Id, CreateTaskCompletionSourceFromWorkflow(workflow));
                            }
                        }
                    });
            }
        }

        private async Task<T> CreateWorkflowRepositoryAndDoAction<T>(Func<IWorkflowRepository, Task<T>> action)
        {
            var repo = _workflowRepoFactory();
            using (repo as IDisposable)
            {
                return await action(repo);
            }
        }

        private class PreloadWorkflow : WorkflowBase
        {
            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField(IsTransient = true)]
            public int PreloadHours { get; set; }

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField(IsTransient = true)]
            public WorkflowEngine WorkflowEngine { get; set; }

            // ReSharper disable once FunctionNeverReturns
            protected override async Task RunAsync()
            {
                while (true)
                {
                    await this.WaitForDate(Utilities.SystemClock.Now.AddHours(PreloadHours).AddMinutes(-30));
                    await WorkflowEngine.PreloadWorkflows(false, PreloadHours);
                }
            }
        }
    }
}
