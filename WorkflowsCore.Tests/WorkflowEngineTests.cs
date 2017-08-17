using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowEngineTests : IDisposable
    {
        private readonly WorkflowEngine _workflowEngine;
        private readonly WorkflowRepository _workflowRepo;

        public WorkflowEngineTests()
        {
            Utilities.SystemClock = new TestingSystemClock();
            _workflowRepo = new WorkflowRepository();
            _workflowEngine = new WorkflowEngine(new DependencyInjectionContainer(_workflowRepo), () => _workflowRepo);
        }

        public void Dispose()
        {
            ((IDisposable)_workflowEngine).Dispose();
            Assert.False(_workflowEngine.RunningWorkflows.Any());
        }

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
                new Dictionary<string, object> { ["Id"] = 1, ["BypassDates"] = true },
                new Dictionary<string, object> { ["TransientId"] = 3, ["TransientBypassDates"] = false });
            await workflow.CompletedTask;
        }

        [Fact]
        public async Task LoadAndExecuteActiveWorkflowsAsyncShouldGetActiveWorkflowsAndExecuteThem()
        {
            _workflowRepo.ActiveWorkflows = new[]
            {
                new WorkflowInstance(
                    1,
                    WorkflowStatus.InProgress,
                    typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                    new Dictionary<string, object> { ["Id"] = 1 }),
                new WorkflowInstance(
                    2,
                    WorkflowStatus.InProgress,
                    typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                    new Dictionary<string, object> { ["Id"] = 2 })
            };

            await _workflowEngine.LoadAndExecuteActiveWorkflowsAsync(4);
            Assert.Null(_workflowRepo.Exception);

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
        public async Task LoadAndExecuteActiveWorkflowsAsyncShouldHandleExceptionsOnWorkflowsLoading()
        {
            await _workflowEngine.LoadAndExecuteActiveWorkflowsAsync(4);
            var ex = await Record.ExceptionAsync(() => _workflowEngine.PreloadWorkflowsTask);
            Assert.IsType<NotImplementedException>(ex);
        }

        [Fact]
        public async Task LoadAndExecuteActiveWorkflowsAsyncShouldPreloadWorkflowsRegularly()
        {
            _workflowRepo.ActiveWorkflows = new[]
            {
                new WorkflowInstance(
                    1,
                    WorkflowStatus.InProgress,
                    typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                    new Dictionary<string, object> { ["Id"] = 1 })
            };

            await _workflowEngine.LoadAndExecuteActiveWorkflowsAsync(4);
            Assert.Null(_workflowRepo.Exception);

            _workflowRepo.ActiveWorkflows = new[]
            {
                new WorkflowInstance(
                    2,
                    WorkflowStatus.InProgress,
                    typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                    new Dictionary<string, object> { ["Id"] = 2 })
            };
            var newTime = TestingSystemClock.Current.Now.AddHours(4).AddMinutes(-30);
            _workflowRepo.MaxActivationDate = newTime.ToUniversalTime().AddHours(4);
            _workflowRepo.IgnoreWorkflowsIds = Enumerable.Repeat((object)1, 1);

            await Task.Delay(5);
            var t = _workflowRepo.InitGetActiveWorkflowsTask();
            TestingSystemClock.Current.SetCurrentTime(newTime);

            await t;

            var workflow = await _workflowEngine.GetActiveWorkflowByIdAsync(2);
            Assert.Null(_workflowRepo.Exception);
            Assert.NotNull(workflow);

            var runningWorkflows = _workflowEngine.RunningWorkflows.Cast<TestWorkflowWithLoad>().ToList();
            Assert.Equal(2, runningWorkflows.Count);
            await Task.WhenAll(runningWorkflows.Select(CancelWorkflowAsync));
        }

        [Fact]
        public async Task LoadAndExecuteActiveWorkflowsMayBeCalledOnlyOnce()
        {
            _workflowRepo.ActiveWorkflows = new[]
            {
                new WorkflowInstance(
                    1,
                    WorkflowStatus.InProgress,
                    typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                    new Dictionary<string, object> { ["Id"] = 1 })
            };

            await _workflowEngine.LoadAndExecuteActiveWorkflowsAsync(4);
            Assert.Null(_workflowRepo.Exception);

            var runningWorkflows = _workflowEngine.RunningWorkflows.Cast<TestWorkflowWithLoad>().ToList();
            await Task.WhenAll(runningWorkflows.Select(CancelWorkflowAsync));

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => _workflowEngine.LoadAndExecuteActiveWorkflowsAsync());

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task GetActiveWorkflowByIdAsyncShouldReturnRunningWorkflow()
        {
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            var id = await workflow.GetIdAsync();

            Assert.Same(workflow, await _workflowEngine.GetActiveWorkflowByIdAsync(id));
            await CancelWorkflowAsync(workflow);
            Assert.Null(await _workflowEngine.GetActiveWorkflowByIdAsync(id));
        }

        [Fact]
        public async Task GetActiveWorkflowByIdAsyncShouldLoadActiveWorkflowIfItIsNotRunningYet()
        {
            _workflowRepo.LoadOnDemandWorkflowId = 3;
            var workflow = await _workflowEngine.GetActiveWorkflowByIdAsync(3);
            Assert.NotNull(workflow);

            var id = await workflow.GetIdAsync();
            Assert.Equal(3, id);
            Assert.Same(workflow, await _workflowEngine.GetActiveWorkflowByIdAsync(3));

            await CancelWorkflowAsync(workflow);
            _workflowRepo.LoadOnDemandWorkflowId = null;
            Assert.Null(await _workflowEngine.GetActiveWorkflowByIdAsync(3));
        }

        [Fact]
        public async Task GetActiveWorkflowByIdAsyncShouldHandleExceptionsOnWorkflowLoad()
        {
            _workflowRepo.FailLoadingOnDemand = true;
            var ex = await Record.ExceptionAsync(() => _workflowEngine.GetActiveWorkflowByIdAsync(3));

            Assert.IsType<InvalidOperationException>(ex);
        }

        private static async Task CancelWorkflowAsync(WorkflowBase workflow)
        {
            workflow.CancelWorkflow();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => workflow.CompletedTask);

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
            private TaskCompletionSource<bool> _tcs;

            public IList<WorkflowInstance> ActiveWorkflows { get; set; }

            public DateTimeOffset MaxActivationDate { get; set; } = Utilities.SystemClock.UtcNow.AddHours(4);

            public IEnumerable<object> IgnoreWorkflowsIds { get; set; } = Enumerable.Empty<object>();

            public Exception Exception { get; private set; }

            public object LoadOnDemandWorkflowId { get; set; }

            public bool FailLoadingOnDemand { get; set; }

            public Task InitGetActiveWorkflowsTask()
            {
                _tcs = new TaskCompletionSource<bool>();
                return _tcs.Task;
            }

            public Task<IList<WorkflowInstance>> GetActiveWorkflowsAsync(
                DateTimeOffset maxActivationDate,
                IEnumerable<object> ignoreWorkflowsIds)
            {
                if (ActiveWorkflows != null)
                {
                    try
                    {
                        Assert.Equal(MaxActivationDate, maxActivationDate);
                        Assert.Equal(IgnoreWorkflowsIds, ignoreWorkflowsIds);
                    }
                    catch (Exception ex)
                    {
                        Exception = ex;
                    }

                    _tcs?.SetResult(true);
                    return Task.FromResult(ActiveWorkflows);
                }

                return Task.FromException<IList<WorkflowInstance>>(new NotImplementedException());
            }

            public Task<WorkflowInstance> GetActiveWorkflowByIdAsync(object workflowId)
            {
                if (FailLoadingOnDemand)
                {
                    return Task.FromException<WorkflowInstance>(new InvalidOperationException());
                }

                if (Equals(workflowId, LoadOnDemandWorkflowId))
                {
                    return Task.FromResult(new WorkflowInstance(
                        workflowId,
                        WorkflowStatus.InProgress,
                        typeof(TestWorkflowWithLoad).AssemblyQualifiedName,
                        new Dictionary<string, object> { ["Id"] = 1 }));
                }

                return Task.FromResult<WorkflowInstance>(null);
            }

            public override void SaveWorkflowData(WorkflowBase workflow, DateTimeOffset? nextActivationDate) =>
                workflow.Id = ++_workflowsSaved;

            public override void MarkWorkflowAsCompleted(WorkflowBase workflow) => Assert.NotNull(workflow);

            public override void MarkWorkflowAsCanceled(WorkflowBase workflow, Exception exception) =>
                Assert.NotNull(workflow);
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

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField(IsTransient = true)]
            private int TransientId { get; set; }

            protected override void OnLoaded()
            {
                throw new NotImplementedException();
            }

            protected override Task RunAsync()
            {
                Assert.Equal(1, Id);
                Assert.True(BypassDates);
                Assert.Equal(3, TransientId);
                Assert.False(Metadata.GetTransientDataField<bool>(this, "TransientBypassDates"));
                return Task.Delay(1);
            }
        }

        private class TestWorkflowWithLoad : WorkflowBase
        {
            public TestWorkflowWithLoad(Func<IWorkflowStateRepository> workflowRepoFactory)
                : base(workflowRepoFactory)
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
