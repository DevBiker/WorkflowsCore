using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorkflowsCore
{
    public class WorkflowEngine : IWorkflowEngine
    {
        private readonly IDependencyInjectionContainer _diContainer;
        private readonly Func<IWorkflowRepository> _workflowRepoFactory;
        private readonly HashSet<WorkflowBase> _runningWorkflows = new HashSet<WorkflowBase>();
        private readonly Dictionary<object, WorkflowBase> _workflowsById = new Dictionary<object, WorkflowBase>();
        private readonly object _loadingTaskLock = new object();
        private Task _loadingTask;

        public WorkflowEngine(
            IDependencyInjectionContainer diContainer, Func<IWorkflowRepository> workflowRepoFactory)
        {
            _diContainer = diContainer;
            _workflowRepoFactory = workflowRepoFactory;
        }

        public Task LoadingTask
        {
            get
            {
                lock (_loadingTaskLock)
                {
                    return _loadingTask;
                }
            }
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

        public WorkflowBase CreateWorkflow(string fullTypeName)
        {
            return CreateWorkflow(fullTypeName, null);
        }

        public WorkflowBase CreateWorkflow(string fullTypeName, IReadOnlyDictionary<string, object> initialWorkflowData)
        {
            return CreateWorkflowCore(fullTypeName, initialWorkflowData: initialWorkflowData);
        }

        public Task LoadAndExecuteActiveWorkflowsAsync()
        {
            lock (_loadingTaskLock)
            {
                if (_loadingTask != null)
                {
                    throw new InvalidOperationException();
                }

                var workflows =
                    _workflowRepoFactory()
                        .GetActiveWorkflows()
                        .Select(w => CreateWorkflowCore(w.WorkflowTypeName, loadedWorkflowData: w.Data, id: w.Id));

                _loadingTask = Task.WhenAll(workflows.Select(w => w.StartedTask));
                return _loadingTask;
            }
        }

        public WorkflowBase GetActiveWorkflowById(object id)
        {
            lock (_loadingTaskLock)
            {
                if (!(_loadingTask?.IsCompleted ?? true))
                {
                    throw new InvalidOperationException();
                }
            }

            lock (_workflowsById)
            {
                WorkflowBase workflow;
                if (_workflowsById.TryGetValue(id, out workflow))
                {
                    return workflow;
                }

                var instance = _workflowRepoFactory().GetSleepingOrFaultedWorkflowById(id);
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

        public async Task GetWorkflowCompletedTaskById(object workflowId)
        {
            var status = _workflowRepoFactory().GetWorkflowStatusById(workflowId);
            switch (status)
            {
                case WorkflowStatus.Completed:
                    return;
                case WorkflowStatus.Canceled:
                {
                    var tcs = new TaskCompletionSource<bool>();
                    tcs.SetCanceled();
                    await tcs.Task;
                    return;
                }
            }

            Task loadingTask;
            lock (_loadingTaskLock)
            {
                loadingTask = _loadingTask;
            }

            if (loadingTask != null)
            {
                await loadingTask;
            }

            var workflow = GetActiveWorkflowById(workflowId);
            if (workflow != null)
            {
                await workflow.CompletedTask;
                return;
            }

            status = _workflowRepoFactory().GetWorkflowStatusById(workflowId);
            switch (status)
            {
                case WorkflowStatus.Completed:
                    break;
                case WorkflowStatus.Canceled:
                {
                    var tcs = new TaskCompletionSource<bool>();
                    tcs.SetCanceled();
                    await tcs.Task;
                    break;
                }

                case WorkflowStatus.Failed:
                    await Task.FromException(
                        new InvalidOperationException($"Workflow with id '{workflowId}' has failed"));
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected WorkflowBase CreateWorkflowCore(
            string fullTypeName,
            IReadOnlyDictionary<string, object> initialWorkflowData = null,
            IReadOnlyDictionary<string, object> loadedWorkflowData = null,
            object id = null,
            bool loadingOnDemand = false)
        {
            if (initialWorkflowData != null && loadedWorkflowData != null)
            {
                throw new ArgumentOutOfRangeException();
            }

            var type = Type.GetType(fullTypeName);
            if (type == null)
            {
                throw new InvalidOperationException();
            }

            var workflow = (WorkflowBase)_diContainer.Resolve(type);

            workflow.StartWorkflow(
                id,
                initialWorkflowData,
                loadedWorkflowData,
                beforeWorkflowStarted: () =>
                {
                    if (!loadingOnDemand && workflow.Id != null)
                    {
                        lock (_workflowsById)
                        {
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
    }
}
