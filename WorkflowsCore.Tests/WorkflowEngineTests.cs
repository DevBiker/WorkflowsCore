using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowEngineTests
    {
        private WorkflowEngine _workflowEngine;
        private WorkflowRepository _workflowRepo;

        public WorkflowEngineTests()
        {
            _workflowRepo = new WorkflowRepository();
            _workflowEngine = new WorkflowEngine(new DependencyInjectionContainer(_workflowRepo), () => _workflowRepo);
        }

        [Fact]
        public void CreateWorkflowForNonExistingWorkflowTypeShouldThrowIoe()
        {
            var ex = Record.Exception(
                () => _workflowEngine.CreateWorkflow("Bad type name", new Dictionary<string, object>()));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void CreateWorkflowForWorkflowTypeThatNotInheritsFromWorkflowBaseShouldThrowIce()
        {
            var ex = Record.Exception(
                () =>
                    _workflowEngine.CreateWorkflow(
                        typeof(BadWorkflow).AssemblyQualifiedName,
                        new Dictionary<string, object>()));

            Assert.IsType<InvalidCastException>(ex);
        }

        [Fact]
        public async Task CreateWorkflowShouldWorkWithInitialDataAsNull()
        {
            _workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            var runningWorkflowsTasks = _workflowEngine.RunningWorkflows.Select(w => w.CompletedTask).ToList();
            await Task.WhenAll(runningWorkflowsTasks);
        }

        [Fact]
        public async Task CreateWorkflowShouldStartInputWorkflow()
        {
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            var runningWorkflowsTasks = _workflowEngine.RunningWorkflows.Select(w => w.CompletedTask).ToList();
            Assert.Same(workflow.CompletedTask, runningWorkflowsTasks.Single());
            await Task.WhenAll(runningWorkflowsTasks);
            Assert.False(_workflowEngine.RunningWorkflows.Select(w => w.CompletedTask).Any());
        }

        [Fact]
        public async Task CreateWorkflowShouldInitilizeWorkflowDataFromSpecified()
        {
            _workflowEngine.CreateWorkflow(
                typeof(TestWorkflowWithData).AssemblyQualifiedName,
                new Dictionary<string, object> { ["Id"] = 1, ["BypassDates"] = true });
            var runningWorkflowsTasks = _workflowEngine.RunningWorkflows.Select(w => w.CompletedTask).ToList();
            Assert.True(runningWorkflowsTasks.Any());
            await Task.WhenAll(runningWorkflowsTasks);
        }

        [Fact]
        public async Task WhenWorkflowIsCompletedItShouldMarkedAsSuch()
        {
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            var runningWorkflowsTasks = _workflowEngine.RunningWorkflows.Select(w => w.CompletedTask).ToList();
            Assert.True(runningWorkflowsTasks.Any());
            await Task.WhenAll(runningWorkflowsTasks);
            Assert.Equal(1, _workflowRepo.NumberOfFinishedWorkflows);
            await AssertWorkflowCancellationTokenCanceled(workflow);
        }

        [Fact]
        public async Task WhenWorkflowIsFailedItShouldMarkedAsSuch()
        {
            _workflowRepo.AllowFailedWorkflows = true;
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestFailedWorkflow).AssemblyQualifiedName);
            var runningWorkflowsTasks = _workflowEngine.RunningWorkflows.Select(w => w.CompletedTask).ToList();
            Assert.True(runningWorkflowsTasks.Any());
            try
            {
                await Task.WhenAll(runningWorkflowsTasks);
                Assert.True(false);
            }
            catch (InvalidOperationException)
            {
            }

            Assert.Equal(1, _workflowRepo.NumberOfFailedWorkflows);
            await AssertWorkflowCancellationTokenCanceled(workflow);
        }

        [Fact]
        public async Task EachWorkflowShouldHaveUniqueCancellationTokenSource()
        {
            _workflowEngine.CreateWorkflow(typeof(TestWorkflowWithCancellation).AssemblyQualifiedName);
            _workflowEngine.CreateWorkflow(typeof(TestWorkflowWithCancellation).AssemblyQualifiedName);
            var runningWorkflows = _workflowEngine.RunningWorkflows.Cast<TestWorkflowWithCancellation>().ToList();
            Assert.Equal(2, runningWorkflows.Count);
            await Task.WhenAll(runningWorkflows.Select(w => w.CompletedTask));
            Assert.NotEqual(
                runningWorkflows[0].CurrentCancellationToken,
                runningWorkflows[1].CurrentCancellationToken);
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
            await Task.WhenAll(runningWorkflows.Select(w => w.CompletedTask));
            Assert.True(runningWorkflows.All(w => w.IsLoaded));
            Assert.Equal(1, runningWorkflows[0].Id);
            Assert.Equal(1, runningWorkflows[0].Data["Id"]);
            Assert.Equal(2, runningWorkflows[1].Id);
            Assert.Equal(2, runningWorkflows[1].Data["Id"]);
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
            await workflow.CompletedTask;
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
            await Task.WhenAll(runningWorkflows.Select(w => w.CompletedTask));

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
            await workflow.CompletedTask;
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
        }

        [Fact]
        public async Task UnexpectedCancellationOfChildActivitiesShouldMarkWorkflowAsFaulted()
        {
            _workflowRepo.AllowFailedWorkflows = true;
            _workflowRepo.AllowedExceptionType = typeof(TaskCanceledException);
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestWorkflowWithCanceledChild).AssemblyQualifiedName);
            try
            {
                await workflow.CompletedTask;
                Assert.True(false);
            }
            catch (TaskCanceledException)
            {
            }
            
            Assert.Equal(1, _workflowRepo.NumberOfFailedWorkflows);
            await AssertWorkflowCancellationTokenCanceled(workflow);
        }

        [Fact]
        public async Task StopWorkflowShouldTerminateItAndMarkAsFaultedEvenItDoesNotHandleCancellationProperly()
        {
            _workflowRepo.AllowFailedWorkflows = true;
            _workflowRepo.AllowedExceptionType = typeof(TimeoutException);
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestWorkflowWithInvalidCancellation).AssemblyQualifiedName);
#pragma warning disable 4014
            Task.Delay(1).ContinueWith(_ => workflow.StopWorkflow(new TimeoutException()));
#pragma warning restore 4014
            try
            {
                await workflow.CompletedTask;
                Assert.True(false);
            }
            catch (TimeoutException)
            {
            }

            Assert.Equal(1, _workflowRepo.NumberOfFailedWorkflows);
        }

        [Fact]
        public async Task StopWorkflowShouldTerminateItAndMarkAsFaulted()
        {
            _workflowRepo.AllowFailedWorkflows = true;
            _workflowRepo.AllowedExceptionType = typeof(TimeoutException);
            var workflow =
                _workflowEngine.CreateWorkflow(typeof(TestWorkflowWithProperCancellation).AssemblyQualifiedName);
            workflow.StopWorkflow(new TimeoutException());
            try
            {
                await workflow.CompletedTask;
                Assert.True(false);
            }
            catch (TimeoutException)
            {
            }

            Assert.Equal(1, _workflowRepo.NumberOfFailedWorkflows);
            await AssertWorkflowCancellationTokenCanceled(workflow);
        }

        [Fact]
        public async Task CancelWorkflowShouldTerminateItAndMarkAsCanceled()
        {
            var workflow =
                _workflowEngine.CreateWorkflow(typeof(TestWorkflowWithProperCancellation).AssemblyQualifiedName);
            ((TestWorkflowWithProperCancellation)workflow).AllowOnCanceled = true;
            workflow.CancelWorkflow();
            try
            {
                await workflow.CompletedTask;
                Assert.True(false);
            }
            catch (TaskCanceledException)
            {
            }

            Assert.Equal(1, _workflowRepo.NumberOfCanceledWorkflows);
            Assert.True(((TestWorkflowWithProperCancellation)workflow).OnCanceledWasCalled);
            await AssertWorkflowCancellationTokenCanceled(workflow);
        }

        [Fact]
        public async Task CancelWorkflowShouldTerminateItAndMarkAsCanceledEvenItDoesNotHandleCancellationProperly()
        {
            var workflow = _workflowEngine.CreateWorkflow(typeof(TestWorkflowWithInvalidCancellation).AssemblyQualifiedName);
#pragma warning disable 4014
            Task.Delay(1).ContinueWith(_ => workflow.CancelWorkflow());
#pragma warning restore 4014
            try
            {
                await workflow.CompletedTask;
                Assert.True(false);
            }
            catch (TaskCanceledException)
            {
            }

            Assert.Equal(1, _workflowRepo.NumberOfCanceledWorkflows);
        }

        [Fact]
        public async Task SleepShouldMarkWorkflowAsSleeping()
        {
            var workflow = (TestWorkflow)_workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            await workflow.RunViaWorkflowTaskScheduler(() => workflow.Sleep());
            Assert.Equal(1, _workflowRepo.NumberOfSleepingWorkflows);
        }

        [Fact]
        public async Task CompletedFaultedCanceledWorkflowCannotBeMarkedAsSleeping()
        {
            var workflow = (TestWorkflow)_workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            await workflow.CompletedTask;

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => workflow.RunViaWorkflowTaskScheduler(() => workflow.Sleep(), forceExecution: true));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public async Task WakeShouldMarkWorkflowAsInProgress()
        {
            var workflow = (TestWorkflow)_workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            await workflow.RunViaWorkflowTaskScheduler(() => workflow.Wake());
            Assert.Equal(1, _workflowRepo.NumberOfInProgressWorkflows);
        }

        [Fact]
        public async Task CompletedFaultedCanceledWorkflowCannotBeMarkedAsInProgress()
        {
            var workflow = (TestWorkflow)_workflowEngine.CreateWorkflow(typeof(TestWorkflow).AssemblyQualifiedName);
            await workflow.CompletedTask;

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => workflow.RunViaWorkflowTaskScheduler(() => workflow.Wake(), forceExecution: true));

            Assert.IsType<InvalidOperationException>(ex);
        }

        private async Task AssertWorkflowCancellationTokenCanceled(WorkflowBase workflow)
        {
            try
            {
                await workflow.DoWorkflowTaskAsync(_ => Assert.True(false));
                Assert.True(false);
            }
            catch (TaskCanceledException)
            {
            }
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
                    case nameof(TestFailedWorkflow):
                        return new TestFailedWorkflow(() => _workflowRepo);
                    case nameof(TestWorkflowWithCancellation):
                        return new TestWorkflowWithCancellation(() => _workflowRepo);
                    case nameof(TestWorkflowWithLoad):
                        return new TestWorkflowWithLoad(() => _workflowRepo);
                    case nameof(TestWorkflowWithCanceledChild):
                        return new TestWorkflowWithCanceledChild(() => _workflowRepo);
                    case nameof(TestWorkflowWithProperCancellation):
                        return new TestWorkflowWithProperCancellation(() => _workflowRepo);
                    case nameof(TestWorkflowWithInvalidCancellation):
                        return new TestWorkflowWithInvalidCancellation(() => _workflowRepo);
                    case nameof(BadWorkflow):
                        return new BadWorkflow();
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        private class WorkflowRepository : IWorkflowStateRepository, IWorkflowRepository
        {
            private int _workflowsSaved;

            public int NumberOfFinishedWorkflows { get; private set; }

            public int NumberOfFailedWorkflows { get; private set; }

            public int NumberOfCanceledWorkflows { get; private set; }

            public int NumberOfSleepingWorkflows { get; private set; }

            public int NumberOfInProgressWorkflows { get; private set; }

            public bool AllowFailedWorkflows { get; set; }

            public Type AllowedExceptionType { get; set; } = typeof(InvalidOperationException);

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

            public void SaveWorkflowData(WorkflowBase workflow)
            {
                workflow.Id = ++_workflowsSaved;
            }

            public void MarkWorkflowAsCompleted(WorkflowBase workflow)
            {
                Assert.NotNull(workflow);
                ++NumberOfFinishedWorkflows;
            }

            public void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception)
            {
                Assert.NotNull(workflow);
                Assert.True(AllowFailedWorkflows);
                Assert.Same(AllowedExceptionType, exception.GetType());
                ++NumberOfFailedWorkflows;
            }

            public void MarkWorkflowAsCanceled(WorkflowBase workflow)
            {
                Assert.NotNull(workflow);
                ++NumberOfCanceledWorkflows;
            }

            public void MarkWorkflowAsSleeping(WorkflowBase workflow)
            {
                Assert.NotNull(workflow);
                ++NumberOfSleepingWorkflows;
            }

            public void MarkWorkflowAsInProgress(WorkflowBase workflow)
            {
                Assert.NotNull(workflow);
                ++NumberOfInProgressWorkflows;
            }
        }

        private class TestWorkflow : WorkflowBase
        {
            public TestWorkflow(Func<IWorkflowStateRepository> workflowRepoFactory) 
                : base(workflowRepoFactory)
            {
            }

            public new void Sleep() => base.Sleep();

            public new void Wake() => base.Wake();

            protected override void OnLoaded()
            {
                throw new NotImplementedException();
            }

            protected override async Task RunAsync()
            {
                await Task.Delay(10);
            }
        }

        private class TestWorkflowWithData : WorkflowBase
        {
            public TestWorkflowWithData(Func<IWorkflowStateRepository> workflowRepoFactory) 
                : base(workflowRepoFactory)
            {
            }

            protected override void OnLoaded()
            {
                throw new NotImplementedException();
            }

            protected override Task RunAsync()
            {
                Assert.Equal(1, GetData<int>("Id"));
                Assert.Equal(true, GetData<bool>("BypassDates"));
                return Task.Delay(1);
            }
        }

        private class TestFailedWorkflow : WorkflowBase
        {
            public TestFailedWorkflow(Func<IWorkflowStateRepository> workflowRepoFactory) 
                : base(workflowRepoFactory)
            {
            }

            protected override void OnLoaded()
            {
                throw new NotImplementedException();
            }

            protected override async Task RunAsync()
            {
                await Task.Delay(1);
                throw new InvalidOperationException();
            }
        }

        private class TestWorkflowWithCancellation : WorkflowBase
        {
            public TestWorkflowWithCancellation(Func<IWorkflowStateRepository> workflowRepoFactory) 
                : base(workflowRepoFactory)
            {
            }

            public CancellationToken CurrentCancellationToken { get; private set; }

            protected override void OnLoaded()
            {
                throw new NotImplementedException();
            }

            protected override async Task RunAsync()
            {
                CurrentCancellationToken = Utilities.CurrentCancellationToken;
                await Task.Delay(1);
                Assert.Equal(CurrentCancellationToken, Utilities.CurrentCancellationToken);
            }
        }

        private class TestWorkflowWithLoad : WorkflowBase
        {
            public TestWorkflowWithLoad(Func<IWorkflowStateRepository> workflowRepoFactory)
                : base(workflowRepoFactory, false)
            {
            }

            public bool IsLoaded { get; private set; }

            public new IReadOnlyDictionary<string, object> Data => base.Data;

            protected override void OnCreated()
            {
                throw new NotImplementedException();
            }

            protected override void OnInit()
            {
                base.OnInit();
                SetData("Id", 3);
            }

            protected override void OnLoaded()
            {
                IsLoaded = true;
                Task.Delay(100).Wait();
            }

            protected override async Task RunAsync()
            {
                await Task.Delay(1000);
            }
        }

        private class TestWorkflowWithCanceledChild : WorkflowBase
        {
            public TestWorkflowWithCanceledChild(Func<IWorkflowStateRepository> workflowRepoFactory)
                : base(workflowRepoFactory)
            {
            }
            
            protected override async Task RunAsync()
            {
                await Task.Delay(1000, new CancellationTokenSource(10).Token);
            }
        }

        private class TestWorkflowWithProperCancellation : WorkflowBase
        {
            public TestWorkflowWithProperCancellation(Func<IWorkflowStateRepository> workflowRepoFactory)
                : base(workflowRepoFactory)
            {
            }

            public bool AllowOnCanceled { get; set; }

            public bool OnCanceledWasCalled { get; private set; }

            protected override void OnCanceled()
            {
                Assert.True(AllowOnCanceled);
                OnCanceledWasCalled = true;
            }

            protected override async Task RunAsync()
            {
                await Task.Delay(1000, Utilities.CurrentCancellationToken);
            }
        }

        private class TestWorkflowWithInvalidCancellation : WorkflowBase
        {
            public TestWorkflowWithInvalidCancellation(Func<IWorkflowStateRepository> workflowRepoFactory)
                : base(workflowRepoFactory)
            {
            }

            protected override async Task RunAsync()
            {
                await Task.Delay(100);
            }
        }

        private class BadWorkflow
        {
        }
    }
}
