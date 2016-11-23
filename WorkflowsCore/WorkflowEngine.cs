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
        private readonly Dictionary<object, WorkflowBase> _workflowsById = new Dictionary<object, WorkflowBase>();
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

        public Task LoadAndExecuteActiveWorkflowsAsync(int preloadHours)
        {
            PreloadWorkflows(true, preloadHours);

            _preloadWorkflow.StartWorkflow(
                initialWorkflowTransientData:
                    new Dictionary<string, object>
                    {
                        [nameof(PreloadWorkflow.PreloadHours)] = preloadHours,
                        [nameof(PreloadWorkflow.WorkflowEngine)] = this
                    });

            return _preloadWorkflow.StartedTask;
        }

        public WorkflowBase GetActiveWorkflowById(object id)
        {
            lock (_workflowsById)
            {
                WorkflowBase workflow;
                if (_workflowsById.TryGetValue(id, out workflow))
                {
                    return workflow;
                }

                var instance = _workflowRepoFactory().GetActiveWorkflowById(id);
                if (instance == null)
                {
                    return null;
                }

                workflow = CreateWorkflowCore(
                    instance.WorkflowTypeName,
                    loadedWorkflowData: instance.Data,
                    id: instance.Id,
                    loadingOnDemand: true);

                _workflowsById.Add(instance.Id, workflow);
                return workflow;
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

                            _workflowsById.Add(workflow.Id, workflow);
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

        private void PreloadWorkflows(bool checkLoadingStarted, int preloadHours)
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

                var workflowInstances = _workflowRepoFactory()
                    .GetActiveWorkflows(
                        Utilities.TimeProvider.Now.AddHours(preloadHours),
                        RunningWorkflows.Where(w => w.Id != null).Select(w => w.Id));

                foreach (var i in workflowInstances)
                {
                    var workflow = CreateWorkflowCore(
                        i.WorkflowTypeName,
                        loadedWorkflowData: i.Data,
                        id: i.Id,
                        loadingOnDemand: true);

                    _workflowsById.Add(i.Id, workflow);
                }
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
                    await this.WaitForDate(Utilities.TimeProvider.Now.AddHours(PreloadHours).AddMinutes(-30));
                    WorkflowEngine.PreloadWorkflows(false, PreloadHours);
                }
            }
        }
    }
}
