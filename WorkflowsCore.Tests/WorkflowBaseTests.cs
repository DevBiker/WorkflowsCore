using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowBaseTests
    {
        public class StartedStateInitializedTasksTests : BaseWorkflowTest<TestWorkflow>
        {
            [Fact]
            public async Task StartedTaskShouldBeCompletedWhenWorkflowIsStarted()
            {
                Workflow = new TestWorkflow();
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                StartWorkflow();
                await Workflow.StartedTask;
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task StartedTaskShouldBeCanceledIfStartingIsFailed()
            {
                Workflow = new TestWorkflow(null, false, allowOnFaulted: true);
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                StartWorkflow(beforeWorkflowStarted: () => { throw new InvalidOperationException(); });
                await Task.WhenAny(Workflow.StartedTask, Task.Delay(100));
                
                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => Workflow.CompletedTask);

                Assert.Equal(TaskStatus.Canceled, Workflow.StartedTask.Status);
                Assert.IsType<InvalidOperationException>(ex);
            }

            [Fact]
            public async Task IfWorkflowIsCanceledThenStartedTaskShouldBeCanceledIfItIsNotCompletedYet()
            {
                Workflow = new TestWorkflow(null, false);
                Workflow.CancelWorkflow();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => Task.WhenAny(Workflow.StartedTask, Task.Delay(100)).Unwrap());

                Assert.IsType<TaskCanceledException>(ex);
            }

            [Fact]
            public async Task IfWorkflowIsCanceledThenInitializedTaskShouldBeCanceledIfItIsNotCompletedYet()
            {
                Workflow = new TestWorkflow(null, false);
                Workflow.CancelWorkflow();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => Task.WhenAny(Workflow.StateInitializedTask, Task.Delay(100)).Unwrap());

                Assert.IsType<TaskCanceledException>(ex);
            }
        }

        public class StartOperationTests : BaseWorkflowTest<TestWorkflow>
        {
            [Fact]
            public async Task IfNoOperationIsStartedWorkflowShouldBeReady()
            {
                Workflow = new TestWorkflow();
                StartWorkflow();

                await Workflow.DoWorkflowTaskAsync(
                    () =>
                    {
                        Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);

                        using (Workflow.TryStartOperation())
                        {
                            Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                        }

                        Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                    });

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task StartingParallelOperationShoulReturnNull()
            {
                Workflow = new TestWorkflow();
                StartWorkflow();

                var operation = await Workflow.DoWorkflowTaskAsync(() => Workflow.TryStartOperation());
                var operation2 = await Workflow.DoWorkflowTaskAsync(() => Workflow.TryStartOperation());

                Assert.NotNull(operation);
                operation.Dispose();
                Assert.Null(operation2);

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task StartingInnerOperationShoulSucceed()
            {
                Workflow = new TestWorkflow();
                StartWorkflow();

                await Workflow.DoWorkflowTaskAsync(
                    async () =>
                    {
                        IDisposable outerOp;
                        using (outerOp = Workflow.TryStartOperation())
                        {
                            await Task.Delay(1);

                            using (var innerOp = Workflow.TryStartOperation())
                            {
                                Assert.Same(outerOp, innerOp);
                            }

                            Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                        }

                        Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);

                        using (var outerOp2 = Workflow.TryStartOperation())
                        {
                            Assert.NotSame(outerOp, outerOp2);
                        }

                        Assert.Equal(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                    }).Unwrap();

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task IfWorkflowCanceledThenReadyTaskIsCanceled()
            {
                Workflow = new TestWorkflow();
                StartWorkflow();

                await Workflow.StartedTask;

                await CancelWorkflowAsync();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => Workflow.ReadyTask);

                Assert.IsType<TaskCanceledException>(ex);
            }

            [Fact]
            public async Task IfWorkflowCanceledThenStartingNewOperationShouldReturnNull()
            {
                Workflow = new TestWorkflow();
                StartWorkflow();

                await Workflow.StartedTask;

                await CancelWorkflowAsync();

                var operation =
                    await Workflow.DoWorkflowTaskAsync(() => Workflow.TryStartOperation(), forceExecution: true);

                Assert.Null(operation);
            }
        }

        public class GeneralTests : BaseWorkflowTest<TestWorkflow>
        {
            public GeneralTests()
            {
                Workflow = new TestWorkflow();
            }

            [Fact]
            public void WorkflowIdCouldBeSetOnlyOnce()
            {
                Workflow.Id = 1;

                var ex = Record.Exception(() => Workflow.Id = 2);

                Assert.Equal(1, Workflow.Id);
                Assert.IsType<InvalidOperationException>(ex);
            }

            [Fact]
            public async Task GetIdShouldReturnWorkflowId()
            {
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(w => Workflow.Id = 1);
                var id = await Workflow.GetIdAsync();
                Assert.Equal(1, (int)id);
                await CancelWorkflowAsync();
            }
        }

        public class DoWorkflowTaskTests : BaseWorkflowTest<TestWorkflow>
        {
            public DoWorkflowTaskTests()
            {
                Workflow = new TestWorkflow();
            }

            [Fact]
            public async Task DoWorkflowTaskShouldRestoreWorkflowCancellationToken()
            {
                var ct = Utilities.CurrentCancellationToken;
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(
                    w =>
                    {
                        Assert.False(Utilities.CurrentCancellationToken.IsCancellationRequested);
                        Assert.NotEqual(ct, Utilities.CurrentCancellationToken);
                    });
                Assert.Equal(ct, Utilities.CurrentCancellationToken);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task DoWorkflowTask2ShouldRestoreWorkflowCancellationToken()
            {
                var ct = Utilities.CurrentCancellationToken;
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(
                    w =>
                    {
                        Assert.False(Utilities.CurrentCancellationToken.IsCancellationRequested);
                        Assert.NotEqual(ct, Utilities.CurrentCancellationToken);
                        return 1;
                    });
                Assert.Equal(ct, Utilities.CurrentCancellationToken);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task DoWorkflowTaskShouldWaitUntilWorkflowIsStarted()
            {
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                var t = Workflow.DoWorkflowTaskAsync(
                    () => Assert.Equal(TaskStatus.RanToCompletion, Workflow.StartedTask.Status));
                await Task.Delay(100);
                StartWorkflow();
                await t;

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task DoWorkflowTask2ShouldWaitUntilWorkflowIsStarted()
            {
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                var t = Workflow.DoWorkflowTaskAsync(
                    w =>
                    {
                        Assert.Equal(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                        return 1;
                    });
                await Task.Delay(100);
                StartWorkflow();
                await t;

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task DoWorkflowTaskShouldExecuteTaskEvenIfWorkflowIsInCacellationIfForceIsSpecified()
            {
                Workflow.CancelWorkflow();
                await Workflow.DoWorkflowTaskAsync(_ => { }, true);
                await Workflow.DoWorkflowTaskAsync(_ => 1, true);
            }

            [Fact]
            public async Task RunViaWorkflowTaskSchedulerShouldCompleteSyncIfRunFromWorkflowThread()
            {
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(
                    () =>
                    {
                        var isRun = false;
                        Assert.Equal(Task.CompletedTask, Workflow.RunViaWorkflowTaskScheduler(() => { isRun = true; }));
                        Assert.True(isRun);
                    });
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task RunViaWorkflowTaskSchedulerShouldCompleteAsyncIfRunOutsideOfWorkflowThread()
            {
                StartWorkflow();
                var isRun = false;
                var task = Workflow.RunViaWorkflowTaskScheduler(() => { isRun = true; });
                await task;

                Assert.NotEqual(Task.CompletedTask, task);
                Assert.True(isRun);

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task RunViaWorkflowTaskScheduler2ShouldCompleteSyncIfRunFromWorkflowThread()
            {
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(
                    () =>
                    {
                        var task = Workflow.RunViaWorkflowTaskScheduler(() => true);
                        Assert.Equal(TaskStatus.RanToCompletion, task.Status);
                        Assert.True(task.Result);
                    });
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task RunViaWorkflowTaskScheduler2ShouldCompleteAsyncIfRunOutsideOfWorkflowThread()
            {
                Assert.False(Workflow.IsWorkflowTaskScheduler);
                var task = Workflow.RunViaWorkflowTaskScheduler(() => true, forceExecution: true);
                Assert.NotEqual(Task.CompletedTask, task);
                var res = await task;
                Assert.True(res);
            }

            [Fact]
            public async Task EnsureWorkflowTaskSchedulerShouldThrowIoeForOtherThreads()
            {
                StartWorkflow();
                await Workflow.DoWorkflowTaskAsync(() => Workflow.EnsureWorkflowTaskScheduler());

                var ex = Record.Exception(() => Workflow.EnsureWorkflowTaskScheduler());

                Assert.IsType<InvalidOperationException>(ex);

                await CancelWorkflowAsync();
            }
        }

        public class ActionsTests : BaseWorkflowTest<TestWorkflow>
        {
            public ActionsTests()
            {
                Workflow = new TestWorkflow();
            }

            [Fact]
            public async Task ConfiguredActionShouldBeExecutedOnRequestWithOperationStarted()
            {
                Workflow.ConfigureAction(
                    "Action 1",
                    () =>
                    {
                        Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                        return 2;
                    });
                StartWorkflow();

                var res = await Workflow.ExecuteActionAsync<int>("Action 1");

                Assert.Equal(2, res);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task ConfiguredActionHandlerShouldBeInvokedWithPassedParameters()
            {
                NamedValues invocationParameters = null;
                Workflow.ConfigureAction(
                    "Action 1",
                    p =>
                    {
                        Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.ReadyTask.Status);
                        invocationParameters = p;
                    },
                    synonym: "Action Synonym");
                StartWorkflow();

                var parameters = new Dictionary<string, object> { ["Id"] = 1 };
                await Workflow.ExecuteActionAsync("Action Synonym", parameters);

                Assert.Equal(parameters, invocationParameters.Data);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task NonConfiguredActionsShouldThrowAoore()
            {
                Workflow.ConfigureAction("Action 1", () => 2);
                StartWorkflow();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync("Action 2"));

                Assert.IsType<ArgumentOutOfRangeException>(ex);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task IfActionNotAllowedAndThrowNotAllowedSpecifiedThenActionExecutionShouldThrowIoe()
            {
                Workflow.ConfigureAction("Action 1", () => 2);
                StartWorkflow();
                Workflow.ActionsAllowed = false;

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync("Action 1"));

                Assert.IsType<InvalidOperationException>(ex);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task IfActionNotAllowedAndThrowNotAllowedDisabledThenActionExecutionShouldSkipAction()
            {
                Workflow.ConfigureAction("Action 1", () => 2);
                StartWorkflow();
                Workflow.ActionsAllowed = false;

                await Workflow.ExecuteActionAsync("Action 1", throwNotAllowed: false);
                await CancelWorkflowAsync();
            }

            [Fact]
            public void ConfiguringTheSameActionMultipleTimesShouldThrowIoe()
            {
                Workflow.ConfigureAction("Action 1", () => 2);

                var ex = Record.Exception(() => Workflow.ConfigureAction("Action 1", () => 3));

                Assert.IsType<InvalidOperationException>(ex);
            }

            [Fact]
            public async Task ActionCouldBeInvokedViaSynonym()
            {
                Workflow.ConfigureAction("Action 1", () => 3, synonyms: new[] { "Action First" });
                StartWorkflow();

                var res = await Workflow.ExecuteActionAsync<int>("Action First");
                Assert.Equal(3, res);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task ExecuteActionShouldWaitUntilWorkflowInitializationCompleted()
            {
                Workflow = new TestWorkflow(null, false);
                Workflow.ConfigureAction(
                    "Action 1",
                    () =>
                    {
                        Assert.Equal(TaskStatus.RanToCompletion, Workflow.StateInitializedTask.Status);
                        return 1;
                    });
                StartWorkflow();
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StateInitializedTask.Status);

                var t = Workflow.ExecuteActionAsync<int>("Action 1");
                await Task.Delay(100);
                Workflow.SetStateInitialized();
                await t;

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task GetAvailableActionsShouldReturnActionsInOrderOfConfiguringOfThem()
            {
                Workflow.ConfigureAction("Action 1", () => 1);
                Workflow.ConfigureAction("Action 2", () => 2);
                Workflow.ConfigureAction("Action 0", () => 0);
                StartWorkflow();

                var res = await Workflow.GetAvailableActionsAsync();

                Assert.Equal(new[] { "Action 1", "Action 2", "Action 0" }, res.ToArray());
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task GetAvailableActionsShouldSkipNotAllowedActions()
            {
                Workflow.ConfigureAction("Action 1", () => 1);
                Workflow.ConfigureAction("Action 2", () => 2);
                Workflow.ConfigureAction("Action 0", () => 0);
                Workflow.NotAllowedAction = "Action 2";
                StartWorkflow();

                var res = await Workflow.GetAvailableActionsAsync();
                Assert.Equal(new[] { "Action 1", "Action 0" }, res.ToArray());
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task GetAvailableActionsShouldNotReturnSynonyms()
            {
                Workflow.ConfigureAction("Action 1", () => 1, synonyms: new[] { "Action First" });
                StartWorkflow();

                var res = await Workflow.GetAvailableActionsAsync();
                Assert.Equal(new[] { "Action 1" }, res.ToArray());
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task GetAvailableActionsShouldNotReturnHiddenActions()
            {
                Workflow.ConfigureAction("Action 0", () => 0);
                Workflow.ConfigureAction("Action 1", () => 1, isHidden: true);
                Workflow.ConfigureAction("Action 2", () => 2);
                StartWorkflow();

                var res = await Workflow.GetAvailableActionsAsync();

                Assert.Equal(new[] { "Action 0", "Action 2" }, res.ToArray());
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task GetAvailableActionsShouldWaitUntilWorkflowInitializationCompleted()
            {
                Workflow = new TestWorkflow(null, false);
                Workflow.ConfigureAction("Action 1", () => 1);
                StartWorkflow();

                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StateInitializedTask.Status);

                var t = Workflow.GetAvailableActionsAsync();
                await Task.Delay(100);
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StateInitializedTask.Status);
                Workflow.SetStateInitialized();
                await t;

                Assert.Equal(TaskStatus.RanToCompletion, Workflow.StateInitializedTask.Status);
                await CancelWorkflowAsync();
            }

            [Fact]
            public void ActionMetadataCouldBeRetrieved()
            {
                Workflow.ConfigureAction(
                    "Action 1",
                    () => 0,
                    metadata: new Dictionary<string, object> { ["Metadata1"] = "Something" });

                var metadata = Workflow.GetActionMetadata("Action 1");

                Assert.True(metadata.GetData<string>("Metadata1") == "Something");
            }

            [Fact]
            public async Task WhenActionExecutedSaveWorkflowDataShouldBeCalled()
            {
                var workflowRepo = new WorkflowRepository();
                Workflow = new TestWorkflow(() => workflowRepo);
                Workflow.ConfigureAction("Action 1", () => 0);
                StartWorkflow();

                await Workflow.StartedTask;
                Assert.Equal(1, workflowRepo.SaveWorkflowDataCounter);

                await Workflow.ExecuteActionAsync("Action 1");

                Assert.Equal(2, workflowRepo.SaveWorkflowDataCounter);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task WhenActionExecutedItsCounterShouldBeIncremented()
            {
                Workflow.ConfigureAction("Action 1", _ => { });
                StartWorkflow();
                await Workflow.StartedTask;

                Assert.Equal(false, Workflow.WasExecuted("Action 1"));
                Assert.Equal(0, Workflow.TimesExecuted("Action 1"));

                await Workflow.ExecuteActionAsync("Action 1");

                Assert.Equal(true, Workflow.WasExecuted("Action 1"));
                Assert.Equal(1, Workflow.TimesExecuted("Action 1"));

                await Workflow.ExecuteActionAsync("Action 1");

                Assert.Equal(true, Workflow.WasExecuted("Action 1"));
                Assert.Equal(2, Workflow.TimesExecuted("Action 1"));

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task ClearTimesExecutedShouldRestActionCounter()
            {
                Workflow.ConfigureAction("Action 1", _ => { });
                StartWorkflow();

                await Workflow.ExecuteActionAsync("Action 1");

                Assert.Equal(true, Workflow.WasExecuted("Action 1"));
                Assert.Equal(1, Workflow.TimesExecuted("Action 1"));

                Workflow.ClearTimesExecuted("Action 1");

                Assert.Equal(false, Workflow.WasExecuted("Action 1"));
                Assert.Equal(0, Workflow.TimesExecuted("Action 1"));

                await CancelWorkflowAsync();
            }
        }

        public class TerminalStatusesTests : BaseWorkflowTest<TestWorkflow>
        {
            private readonly WorkflowRepository _workflowRepo = new WorkflowRepository();

            [Fact]
            public async Task WhenWorkflowIsCompletedItShouldMarkedAsSuch()
            {
                Workflow = new TestWorkflow(() => _workflowRepo, doNotComplete: false, allowOnCanceled: false);
                StartWorkflow();

                await WaitUntilWorkflowCompleted().WaitWithTimeout(1000);

                Assert.Equal(1, _workflowRepo.NumberOfCompletedWorkflows);
                await AssertWorkflowCancellationTokenCanceled();
            }

            [Fact]
            public async Task WhenWorkflowIsFailedItShouldMarkedAsSuch()
            {
                Workflow = new TestWorkflow(() => _workflowRepo, fail: true, allowOnFaulted: true);
                StartWorkflow();

                await WaitUntilWorkflowFailed<ArgumentOutOfRangeException>().WaitWithTimeout(1000);

                Assert.Equal(1, _workflowRepo.NumberOfFailedWorkflows);
                Assert.True(Workflow.OnFaultedWasCalled);
                await AssertWorkflowCancellationTokenCanceled();
            }

            [Fact]
            public async Task EachWorkflowShouldHaveUniqueCancellationTokenSource()
            {
                Workflow = new TestWorkflow(() => _workflowRepo, doNotComplete: false);
                StartWorkflow();

                var workflowRepo = new WorkflowRepository();
                using (var second = new BaseWorkflowTest<TestWorkflow>())
                {
                    second.Workflow = new TestWorkflow(() => workflowRepo, doNotComplete: false);
                    second.StartWorkflow();

                    await Task.WhenAll(WaitUntilWorkflowCompleted(), second.WaitUntilWorkflowCompleted());
                    Assert.NotEqual(
                        Workflow.CurrentCancellationToken,
                        second.Workflow.CurrentCancellationToken);
                }

                Assert.Equal(1, _workflowRepo.NumberOfCompletedWorkflows);
                Assert.Equal(1, workflowRepo.NumberOfCompletedWorkflows);
            }

            [Fact]
            public async Task UnexpectedCancellationOfChildActivitiesShouldMarkWorkflowAsFaulted()
            {
                Workflow = new TestWorkflow(() => _workflowRepo, canceledChild: true, allowOnCanceled: false);
                StartWorkflow();

                await WaitUntilWorkflowFailed<TaskCanceledException>();

                Assert.Equal(1, _workflowRepo.NumberOfFailedWorkflows);
                await AssertWorkflowCancellationTokenCanceled();
            }

            [Fact]
            public async Task StopWorkflowShouldTerminateItAndMarkAsFaultedEvenItDoesNotHandleCancellationProperly()
            {
                Workflow = new TestWorkflow(() => _workflowRepo, badCancellation: true, allowOnFaulted: true);
                StartWorkflow();
                await Workflow.StartedTask;

                Workflow.StopWorkflow(new TimeoutException());
                Workflow.CompleteLongWork();
                await WaitUntilWorkflowFailed<TimeoutException>();

                Assert.Equal(1, _workflowRepo.NumberOfFailedWorkflows);
                await AssertWorkflowCancellationTokenCanceled();
            }

            [Fact]
            public async Task StopWorkflowShouldTerminateItAndMarkAsFaulted()
            {
                Workflow = new TestWorkflow(() => _workflowRepo, allowOnFaulted: true);
                StartWorkflow();
                await Workflow.StartedTask;

                Workflow.StopWorkflow(new TimeoutException());
                await WaitUntilWorkflowFailed<TimeoutException>();

                Assert.Equal(1, _workflowRepo.NumberOfFailedWorkflows);
                await AssertWorkflowCancellationTokenCanceled();
            }

            [Fact]
            public async Task CancelWorkflowShouldTerminateItAndMarkAsCanceledEvenItDoesNotHandleCancellationProperly()
            {
                Workflow = new TestWorkflow(() => _workflowRepo, badCancellation: true);
                StartWorkflow();
                await Workflow.StartedTask;

                var t = CancelWorkflowAsync();
                Workflow.CompleteLongWork();
                await t;

                Assert.Equal(1, _workflowRepo.NumberOfCanceledWorkflows);
                Assert.True(Workflow.OnCanceledWasCalled);
                await AssertWorkflowCancellationTokenCanceled();
            }

            [Fact]
            public async Task CancelWorkflowShouldTerminateItAndMarkAsCanceled()
            {
                Workflow = new TestWorkflow(() => _workflowRepo);
                StartWorkflow();
                await Workflow.StartedTask;

                await CancelWorkflowAsync();

                Assert.Equal(1, _workflowRepo.NumberOfCanceledWorkflows);
                Assert.True(Workflow.OnCanceledWasCalled);
                await AssertWorkflowCancellationTokenCanceled();
            }

            [Fact]
            public async Task CanceledWorkflowShouldNotBeMarkedAsFaulted()
            {
                Workflow = new TestWorkflow(() => _workflowRepo, failCancellation: true);
                StartWorkflow();
                await Workflow.StartedTask;

                await CancelWorkflowAsync();

                Assert.Equal(1, _workflowRepo.NumberOfCanceledWorkflows);
                Assert.True(Workflow.OnCanceledWasCalled);
                await AssertWorkflowCancellationTokenCanceled();
            }

            private async Task AssertWorkflowCancellationTokenCanceled()
            {
                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => Workflow.DoWorkflowTaskAsync(() => Assert.True(false)));

                Assert.IsType<TaskCanceledException>(ex);
            }
        }

        public sealed class TestWorkflow : WorkflowBase
        {
            private readonly bool _canceledChild;
            private readonly bool _allowOnCanceled;
            private readonly bool _allowOnFaulted;
            private readonly bool _badCancellation;
            private readonly bool _failCancellation;
            private readonly TaskCompletionSource<bool> _badCancellationTcs = new TaskCompletionSource<bool>();
            private readonly int _timeout;

            public TestWorkflow(
                Func<IWorkflowStateRepository> workflowRepoFactory = null,
                bool isStateInitializedImmediatelyAfterStart = true,
                bool doNotComplete = true,
                bool fail = false,
                bool canceledChild = false,
                bool allowOnCanceled = true,
                bool badCancellation = false,
                bool failCancellation = false,
                bool allowOnFaulted = false)
                : base(workflowRepoFactory, isStateInitializedImmediatelyAfterStart)
            {
                _canceledChild = canceledChild;
                _allowOnCanceled = allowOnCanceled && !allowOnFaulted;
                _badCancellation = badCancellation;
                _failCancellation = failCancellation;
                _timeout = fail ? -2 : (!doNotComplete ? 1 : Timeout.Infinite);
                _allowOnFaulted = allowOnFaulted;
            }

            public bool ActionsAllowed { get; set; } = true;

            public string NotAllowedAction { get; set; }

            public CancellationToken CurrentCancellationToken { get; private set; }

            public bool OnCanceledWasCalled { get; private set; }

            public bool OnFaultedWasCalled { get; private set; }

            public new void SetStateInitialized() => base.SetStateInitialized();

            // ReSharper disable once UnusedParameter.Local
            public new bool WasExecuted(string action) => base.WasExecuted(action);

            // ReSharper disable once UnusedParameter.Local
            public new int TimesExecuted(string action) => base.TimesExecuted(action);

            // ReSharper disable once UnusedParameter.Local
            public new void ClearTimesExecuted(string action) => base.ClearTimesExecuted(action);

            [SuppressMessage("ReSharper", "UnusedParameter.Local", Justification = "It is OK")]
            public new void ConfigureAction(
                string action,
                Action<NamedValues> actionHandler,
                IReadOnlyDictionary<string, object> metadata = null,
                string synonym = null,
                IEnumerable<string> synonyms = null,
                bool isHidden = false)
            {
                base.ConfigureAction(action, actionHandler, metadata, synonym, synonyms, isHidden);
            }

            [SuppressMessage("ReSharper", "UnusedParameter.Local", Justification = "It is OK")]
            public new void ConfigureAction<T>(
                string action,
                Func<T> actionHandler,
                IReadOnlyDictionary<string, object> metadata = null,
                string synonym = null,
                IEnumerable<string> synonyms = null,
                bool isHidden = false)
            {
                base.ConfigureAction(action, actionHandler, metadata, synonym, synonyms, isHidden);
            }

            // ReSharper disable once UnusedParameter.Local
            public new NamedValues GetActionMetadata(string action) => base.GetActionMetadata(action);

            public new IDisposable TryStartOperation() => base.TryStartOperation();

            public void CompleteLongWork() => _badCancellationTcs.SetResult(true);

            protected override async Task RunAsync()
            {
                CurrentCancellationToken = Utilities.CurrentCancellationToken;

                if (_badCancellation)
                {
                    await _badCancellationTcs.Task.WaitWithTimeout(1000);
                }
                else
                {
                    var cancellationToken = _canceledChild
                        ? new CancellationTokenSource(1).Token
                        : Utilities.CurrentCancellationToken;
                    try
                    {
                        await Task.Delay(_timeout, cancellationToken);
                    }
                    catch (TaskCanceledException) when (_failCancellation)
                    {
                        throw new InvalidOperationException();
                    }
                }

                Assert.Equal(CurrentCancellationToken, Utilities.CurrentCancellationToken);
            }

            protected override bool IsActionAllowed(string action) => ActionsAllowed && action != NotAllowedAction;

            protected override void OnCanceled(Exception exception)
            {
                Assert.True(_allowOnCanceled);
                base.OnCanceled(exception);
                OnCanceledWasCalled = true;
            }

            protected override void OnFaulted(Exception exception)
            {
                Assert.True(_allowOnFaulted);
                base.OnFaulted(exception);
                OnFaultedWasCalled = true;
            }
        }

        private class WorkflowRepository : IWorkflowStateRepository
        {
            public int SaveWorkflowDataCounter { get; private set; }

            public int NumberOfCompletedWorkflows { get; private set; }

            public int NumberOfCanceledWorkflows { get; private set; }

            public int NumberOfFailedWorkflows { get; private set; }

            public void SaveWorkflowData(WorkflowBase workflow, DateTime? nextActivationDate)
            {
                Assert.NotNull(workflow);
                ++SaveWorkflowDataCounter;
            }

            public void MarkWorkflowAsCompleted(WorkflowBase workflow)
            {
                Assert.NotNull(workflow);
                ++NumberOfCompletedWorkflows;
            }

            public void MarkWorkflowAsCanceled(WorkflowBase workflow, Exception exception)
            {
                Assert.NotNull(workflow);
                ++NumberOfCanceledWorkflows;
            }

            public void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception)
            {
                Assert.NotNull(workflow);
                Assert.NotNull(exception);
                ++NumberOfFailedWorkflows;
            }
        }
    }
}
