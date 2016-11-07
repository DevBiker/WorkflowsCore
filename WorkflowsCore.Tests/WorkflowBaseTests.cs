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
            public StartedStateInitializedTasksTests()
            {
                Workflow = new TestWorkflow();
            }

            [Fact]
            public async Task StartedTaskShouldBeCompletedWhenWorkflowIsStarted()
            {
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                StartWorkflow();
                await Workflow.StartedTask;
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task StartedTaskShouldBeCanceledIfStartingIsFailed()
            {
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                StartWorkflow(beforeWorkflowStarted: () => { throw new InvalidOperationException(); });
                await Task.WhenAny(Workflow.StartedTask, Task.Delay(100));
                
                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => Workflow.CompletedTask);

                Assert.Equal(TaskStatus.Canceled, Workflow.StartedTask.Status);
                Assert.IsType(typeof(InvalidOperationException), ex);
            }

            [Fact]
            public async Task IfWorkflowIsCanceledThenStartedTaskShouldBeCanceledIfItIsNotCompletedYet()
            {
                Workflow = new TestWorkflow(null, false);
                Workflow.CancelWorkflow();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => Task.WhenAny(Workflow.StartedTask, Task.Delay(100)).Unwrap());

                Assert.IsType(typeof(TaskCanceledException), ex);
            }

            [Fact]
            public async Task IfWorkflowIsCanceledThenInitializedTaskShouldBeCanceledIfItIsNotCompletedYet()
            {
                Workflow = new TestWorkflow(null, false);
                Workflow.CancelWorkflow();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(
                    () => Task.WhenAny(Workflow.StateInitializedTask, Task.Delay(100)).Unwrap());

                Assert.IsType(typeof(TaskCanceledException), ex);
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
                Assert.IsType(typeof(InvalidOperationException), ex);
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

        public class WorkflowDataTests : BaseWorkflowTest<TestWorkflow>
        {
            public WorkflowDataTests()
            {
                Workflow = new TestWorkflow();
                StartWorkflow();
            }

            [Fact]
            public async Task SetDataForDictionariesShouldOverrideExistingValuesAndIgnoreOthers()
            {
                await Workflow.DoWorkflowTaskAsync(
                    w =>
                    {
                        w.SetData(new Dictionary<string, object> { ["Id"] = 1, ["BypassDates"] = true });
                        w.SetData(new Dictionary<string, object> { ["Id"] = 2 });
                        Assert.Equal(2, w.Data["Id"]);
                        Assert.Equal(true, w.GetData<bool>("BypassDates"));
                    });
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task GetDataForNonExistingKeyShouldReturnDefaultValue()
            {
                await Workflow.DoWorkflowTaskAsync(w => Assert.Equal(0, w.GetData<int>("Id")));
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task SetDataForKeyShouldRemoveKeyIfValueIsDefault()
            {
                await Workflow.DoWorkflowTaskAsync(
                    w =>
                    {
                        w.SetData("BypassDates", true);
                        w.SetData("BypassDates", false);
                        Assert.False(w.Data.ContainsKey("BypassDates"));
                    });
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task SetTransientDataForDictionariesShouldOverrideExistingValuesAndIgnoreOthers()
            {
                await Workflow.DoWorkflowTaskAsync(
                    w =>
                    {
                        w.SetTransientData(new Dictionary<string, object> { ["Id"] = 1, ["BypassDates"] = true });
                        w.SetTransientData(new Dictionary<string, object> { ["Id"] = 2 });
                        Assert.Equal(2, w.TransientData["Id"]);
                        Assert.Equal(true, w.GetTransientData<bool>("BypassDates"));
                    });
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task GetTransientDataForNonExistingKeyShouldReturnDefaultValue()
            {
                await Workflow.DoWorkflowTaskAsync(w => Assert.Equal(0, w.GetTransientData<int>("Id")));
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
                TestUtils.DoAsync(() => StartWorkflow(), delay: 100);
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                await Workflow.DoWorkflowTaskAsync(
                    w => Assert.Equal(TaskStatus.RanToCompletion, Workflow.StartedTask.Status));

                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task DoWorkflowTask2ShouldWaitUntilWorkflowIsStarted()
            {
                TestUtils.DoAsync(() => StartWorkflow(), delay: 100);
                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                await Workflow.DoWorkflowTaskAsync(
                    w =>
                    {
                        Assert.Equal(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);
                        return 1;
                    });

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
            public async Task RunViaWorkflowTaskScheduler2ShouldCompleteASyncIfRunOutsideOfWorkflowThread()
            {
                Assert.False(Workflow.IsWorkflowTaskScheduler);
                var task = Workflow.RunViaWorkflowTaskScheduler(() => true, forceExecution: true);
                Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);
                var res = await task;
                Assert.True(res);
            }
        }

        public class ActionsTests : BaseWorkflowTest<TestWorkflow>
        {
            public ActionsTests()
            {
                Workflow = new TestWorkflow();
            }

            [Fact]
            public async Task ConfiguredActionShouldBeExecutedOnRequest()
            {
                Workflow.ConfigureAction("Action 1", () => 2);
                StartWorkflow();

                var res = await Workflow.ExecuteActionAsync<int>("Action 1");

                Assert.Equal(2, res);
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task ConfiguredActionHandlerShouldBeInvokedWithPassedParameters()
            {
                NamedValues invocationParameters = null;
                Workflow.ConfigureAction("Action 1", p => invocationParameters = p, synonym: "Action Synonym");
                StartWorkflow();

                var parameters = new Dictionary<string, object> { ["Id"] = 1 };
                await Workflow.ExecuteActionAsync("Action Synonym", parameters);

                Assert.True(parameters.SequenceEqual(invocationParameters.Data));
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task NonConfiguredActionsShouldThrowAoore()
            {
                Workflow.ConfigureAction("Action 1", () => 2);
                StartWorkflow();

                // ReSharper disable once PossibleNullReferenceException
                var ex = await Record.ExceptionAsync(() => Workflow.ExecuteActionAsync("Action 2"));

                Assert.IsType(typeof(ArgumentOutOfRangeException), ex);
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

                Assert.IsType(typeof(InvalidOperationException), ex);
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

                Assert.IsType(typeof(InvalidOperationException), ex);
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
                Workflow.ConfigureAction("Action 1", () => 1);
                StartWorkflow();
                TestUtils.DoAsync(() => Workflow.SetStateInitialized(), delay: 100);

                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StateInitializedTask.Status);
                await Workflow.ExecuteActionAsync<int>("Action 1");

                Assert.Equal(TaskStatus.RanToCompletion, Workflow.StateInitializedTask.Status);
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

                Assert.True(res.SequenceEqual(new[] { "Action 1", "Action 2", "Action 0" }));
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
                Assert.True(res.SequenceEqual(new[] { "Action 1", "Action 0" }));
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task GetAvailableActionsShouldNotReturnSynonyms()
            {
                Workflow.ConfigureAction("Action 1", () => 1, synonyms: new[] { "Action First" });
                StartWorkflow();

                var res = await Workflow.GetAvailableActionsAsync();
                Assert.True(res.SequenceEqual(new[] { "Action 1" }));
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

                Assert.True(res.SequenceEqual(new[] { "Action 0", "Action 2" }));
                await CancelWorkflowAsync();
            }

            [Fact]
            public async Task GetAvailableActionsShouldWaitUntilWorkflowInitializationCompleted()
            {
                Workflow = new TestWorkflow(null, false);
                Workflow.ConfigureAction("Action 1", () => 1);
                StartWorkflow();

                TestUtils.DoAsync(() => Workflow.SetStateInitialized(), delay: 100);

                Assert.NotEqual(TaskStatus.RanToCompletion, Workflow.StateInitializedTask.Status);
                await Workflow.GetAvailableActionsAsync();

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

        public sealed class TestWorkflow : WorkflowBase
        {
            public TestWorkflow(
                Func<IWorkflowStateRepository> workflowRepoFactory = null,
                bool isStateInitializedImmediatelyAfterStart = true)
                : base(workflowRepoFactory, isStateInitializedImmediatelyAfterStart)
            {
            }

            public bool ActionsAllowed { get; set; } = true;

            public string NotAllowedAction { get; set; }

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

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);

            protected override bool IsActionAllowed(string action) => ActionsAllowed && action != NotAllowedAction;
        }

        private class WorkflowRepository : DummyWorkflowStateRepository
        {
            public int SaveWorkflowDataCounter { get; private set; }

            public override void SaveWorkflowData(WorkflowBase workflow)
            {
                Assert.NotNull(workflow);
                ++SaveWorkflowDataCounter;
            }

            public override void MarkWorkflowAsCanceled(WorkflowBase workflow) => Assert.NotNull(workflow);
        }
    }
}
