using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowEngineTests : IDisposable
    {
        private readonly WorkflowEngine _workflowEngine;
        private readonly WorkflowRepository _workflowRepo = new WorkflowRepository();

        public WorkflowEngineTests()
        {
            _workflowEngine = new WorkflowEngine(new DependencyInjectionContainer(_workflowRepo), () => _workflowRepo);
        }

        public void Dispose() => Assert.False(_workflowEngine.RunningWorkflows.Any());

        [Fact]
        public void CreateWorkflowForNonExistingWorkflowTypeShouldThrowAoore()
        {
            var ex = Record.Exception(
                () => _workflowEngine.CreateWorkflow("Bad type name", new Dictionary<string, object>()));

            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        [Fact]
        public async Task CreateWorkflowShouldWorkWithInitialDataAsNull()
        {
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            Assert.NotNull(workflow);
            await CancelWorkflowAsync(workflow);
        }

        [Fact]
        public void CreateWorkflowForWorkflowTypeThatNotInheritsFromWorkflowBaseShouldThrowIce()
        {
            var ex = Record.Exception(
                () => _workflowEngine.CreateWorkflow(typeof(BadWorkflow).AssemblyQualifiedName));

            Assert.IsType<InvalidCastException>(ex);
        }

        [Fact]
        public async Task CreateWorkflowShouldStartInputWorkflow()
        {
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            await workflow.StartedTask;
            var runningWorkflowsTasks = _workflowEngine.RunningWorkflows.Select(w => w.CompletedTask).ToList();
            Assert.Same(workflow.CompletedTask, runningWorkflowsTasks.Single());
            await CancelWorkflowAsync(workflow);
        }

        [Fact]
        public async Task CreateWorkflowShouldInitilizeWorkflowDataFromSpecified()
        {
            var workflow = _workflowEngine.CreateWorkflow(
                typeof(TestWorkflowWithData).AssemblyQualifiedName,
                new Dictionary<string, object> { ["Id"] = 1, ["BypassDates"] = true });
            await workflow.CompletedTask;
        }

        [Fact]
        public async Task ExecuteActiveWorkflowsShouldGetActiveWorkflowsAndExecuteThem()
        {
            _workflowRepo.ActiveWorkflows = new[]
            {
                new WorkflowInstance
                {
                    WorkflowTypeName = typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                    Id = 1,
                    Data = new Dictionary<string, object> { ["Id"] = 1 }
                },
                new WorkflowInstance
                {
                    WorkflowTypeName = typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                    Id = 2,
                    Data = new Dictionary<string, object> { ["Id"] = 2 }
                }
            };

            await _workflowEngine.LoadAndExecuteActiveWorkflowsAsync();

            var runningWorkflows = _workflowEngine.RunningWorkflows.Cast<TestWorkflowWithLoad>().ToList();
            Assert.Equal(2, runningWorkflows.Count);
            await Task.WhenAll(runningWorkflows.Select(CancelWorkflowAsync));
            Assert.True(runningWorkflows.All(w => w.IsLoaded));
            Assert.Equal(1, runningWorkflows[0].Id);
            Assert.Equal(1, runningWorkflows[0].GetDataFieldAsync<int>("Id", forceExecution: true).Result);
            Assert.Equal(2, runningWorkflows[1].Id);
            Assert.Equal(2, runningWorkflows[1].GetDataFieldAsync<int>("Id", forceExecution: true).Result);
        }

        [Fact]
        public async Task ExecuteActiveWorkflowsShouldWaitUntilAllWorkflowsAreStarted()
        {
            _workflowRepo.ActiveWorkflows = new[]
            {
                new WorkflowInstance
                {
                    WorkflowTypeName = typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                    Id = 1,
                    Data = new Dictionary<string, object> { ["Id"] = 1 }
                }
            };

            await _workflowEngine.LoadAndExecuteActiveWorkflowsAsync();
            var workflow = _workflowEngine.GetActiveWorkflowById(1);
            Assert.NotNull(workflow);
            Assert.Equal(TaskStatus.RanToCompletion, workflow.StartedTask.Status);
            await CancelWorkflowAsync(workflow);
        }

        [Fact]
        public async Task ExecuteActiveWorkflowsMayBeCalledOnlyOnce()
        {
            _workflowRepo.ActiveWorkflows = new[]
            {
                new WorkflowInstance
                {
                    WorkflowTypeName = typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                    Id = 1,
                    Data = new Dictionary<string, object> { ["Id"] = 1 }
                }
            };

            await _workflowEngine.LoadAndExecuteActiveWorkflowsAsync();
            var runningWorkflows = _workflowEngine.RunningWorkflows.Cast<TestWorkflowWithLoad>().ToList();            
            await Task.WhenAll(runningWorkflows.Select(CancelWorkflowAsync));

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => _workflowEngine.LoadAndExecuteActiveWorkflowsAsync());

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task GetRunningWorkflowByIdShouldReturnRunningWorkflowsOnly()
        {
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            var id = await workflow.GetIdAsync();

            Assert.Same(workflow, _workflowEngine.GetActiveWorkflowById(id));
            await CancelWorkflowAsync(workflow);
            Assert.Null(_workflowEngine.GetActiveWorkflowById(id));
        }

        [Fact]
        public async Task GetRunningWorkflowByIdShouldLoadSleepingWorkflow()
        {
            const int Id = 1000;
            Assert.Null(_workflowEngine.GetActiveWorkflowById(Id));
            _workflowRepo.SleepingWorkflowId = Id;

            var workflow = _workflowEngine.GetActiveWorkflowById(Id);

            Assert.NotNull(workflow);
            await workflow.StartedTask;
            Assert.Equal(Id, workflow.Id);
            await CancelWorkflowAsync(workflow);
        }

        private static async Task CancelWorkflowAsync(WorkflowBase workflow)
        {
            workflow.CancelWorkflow();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => workflow.CompletedTask.WaitWithTimeout(1000));

            Assert.IsType<TaskCanceledException>(ex);
        }

        private class DependencyInjectionContainer : IDependencyInjectionContainer
        {
            private readonly IWorkflowStateRepository _workflowRepo;

            public DependencyInjectionContainer(IWorkflowStateRepository workflowRepo)
            {
                _workflowRepo = workflowRepo;
            }

            public object Resolve(Type type)
            {
                switch (type.Name)
                {
                    case nameof(TestWorkflow):
                        return new TestWorkflow(() => _workflowRepo);
                    case nameof(TestWorkflowWithData):
                        return new TestWorkflowWithData(() => _workflowRepo);
                    case nameof(TestWorkflowWithLoad):
                        return new TestWorkflowWithLoad(() => _workflowRepo);
                    case nameof(BadWorkflow):
                        return new BadWorkflow();
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        private class WorkflowRepository : DummyWorkflowStateRepository, IWorkflowRepository
        {
            private int _workflowsSaved;

            public IList<WorkflowInstance> ActiveWorkflows { get; set; }

            public object SleepingWorkflowId { get; set; }

            public IList<WorkflowInstance> GetActiveWorkflows()
            {
                if (ActiveWorkflows != null)
                {
                    return ActiveWorkflows;
                }

                throw new NotImplementedException();
            }

            public WorkflowInstance GetSleepingOrFaultedWorkflowById(object workflowId)
            {
                if (Equals(workflowId, SleepingWorkflowId))
                {
                    return new WorkflowInstance
                    {
                        WorkflowTypeName = typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                        Id = workflowId,
                        Data = new Dictionary<string, object> { ["Id"] = 1 }
                    };
                }

                return null;
            }

            public WorkflowStatus GetWorkflowStatusById(object workflowId)
            {
                throw new NotImplementedException();
            }

            public override void SaveWorkflowData(WorkflowBase workflow) => workflow.Id = ++_workflowsSaved;

            public override void MarkWorkflowAsCompleted(WorkflowBase workflow) => Assert.NotNull(workflow);

            public override void MarkWorkflowAsCanceled(WorkflowBase workflow) => Assert.NotNull(workflow);
        }

        private class TestWorkflow : WorkflowBase
        {
            public TestWorkflow(Func<IWorkflowStateRepository> workflowRepoFactory) 
                : base(workflowRepoFactory)
            {
            }

            protected override void OnLoaded()
            {
                throw new NotImplementedException();
            }

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }

        private class TestWorkflowWithData : WorkflowBase
        {
            public TestWorkflowWithData(Func<IWorkflowStateRepository> workflowRepoFactory) 
                : base(workflowRepoFactory)
            {
            }

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField]
            private new int Id { get; set; }

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField]
            private bool BypassDates { get; set; }

            protected override void OnLoaded()
            {
                throw new NotImplementedException();
            }

            protected override Task RunAsync()
            {
                Assert.Equal(1, Id);
                Assert.Equal(true, BypassDates);
                return Task.Delay(1);
            }
        }

        private class TestWorkflowWithLoad : WorkflowBase
        {
            public TestWorkflowWithLoad(Func<IWorkflowStateRepository> workflowRepoFactory)
                : base(workflowRepoFactory, false)
            {
            }

            public bool IsLoaded { get; private set; }

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField]
            private new int Id { get; set; }

            protected override void OnCreated()
            {
                throw new NotImplementedException();
            }

            protected override void OnInit()
            {
                base.OnInit();
                Id = 3;
            }

            protected override void OnLoaded() => IsLoaded = true;

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }

        private class BadWorkflow
        {
        }
    }
}
