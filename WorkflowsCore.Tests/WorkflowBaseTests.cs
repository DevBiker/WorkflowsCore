using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowBaseTests
    {
        private readonly WorkflowRepository _workflowRepo = new WorkflowRepository();
        private readonly TestWorkflow _workflow;

        public WorkflowBaseTests()
        {
            _workflow = new TestWorkflow(() => _workflowRepo);
        }

        [Fact]
        public async Task StartedTaskShouldBeCompletedWhenWorkflowIsStarted()
        {
            Assert.NotEqual(TaskStatus.RanToCompletion, _workflow.StartedTask.Status);
            _workflow.StartWorkflow(initialWorkflowData: new Dictionary<string, object>());
            await _workflow.StartedTask;
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task StartedTaskShouldBeCanceledIfStartingIsFailed()
        {
            Assert.NotEqual(TaskStatus.RanToCompletion, _workflow.StartedTask.Status);
            _workflow.StartWorkflow(
                initialWorkflowData: new Dictionary<string, object>(),
                beforeWorkflowStarted: () => { throw new InvalidOperationException(); });
            await Task.WhenAny(_workflow.StartedTask, Task.Delay(100));
            Assert.Equal(TaskStatus.Canceled, _workflow.StartedTask.Status);
        }

        [Fact]
        public async Task IfWorkflowIsCanceledThenStartedTaskShouldBeCanceledIfItIsNotCompletedYet()
        {
            var workflow = new TestWorkflow(() => _workflowRepo, false);
            workflow.CancelWorkflow();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => Task.WhenAny(workflow.StartedTask, Task.Delay(100)).Unwrap());

            Assert.IsType(typeof(TaskCanceledException), ex);
        }

        [Fact]
        public async Task IfWorkflowIsCanceledThenInitializedTaskShouldBeCanceledIfItIsNotCompletedYet()
        {
            var workflow = new TestWorkflow(() => _workflowRepo, false);
            workflow.CancelWorkflow();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Task.WhenAny(workflow.StateInitializedTask, Task.Delay(100)).Unwrap());

            Assert.IsType(typeof(TaskCanceledException), ex);
        }

        [Fact]
        public async Task WorkflowIdCouldBeSetOnlyOnce()
        {
            var ex = await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    _workflow.Id = 1;
                    Assert.Equal(1, _workflow.Id);
                    return Record.Exception(() => _workflow.Id = 2);
                },
                forceExecution: true);

            Assert.IsType(typeof(InvalidOperationException), ex);
        }

        [Fact]
        public async Task GetIdShouldReturnWorkflowId()
        {
            _workflow.StartWorkflow();
            await _workflow.DoWorkflowTaskAsync(w => _workflow.Id = 1);
            var id = await _workflow.GetIdAsync();
            Assert.Equal(1, (int)id);
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task SetDataForDictionariesShouldOverrideExistingValuesAndIgnoreOthers()
        {
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    w.SetData(new Dictionary<string, object> { ["Id"] = 1, ["BypassDates"] = true });
                    w.SetData(new Dictionary<string, object> { ["Id"] = 2 });
                    Assert.Equal(2, w.Data["Id"]);
                    Assert.Equal(true, w.GetData<bool>("BypassDates"));
                },
                forceExecution: true);
        }

        [Fact]
        public async Task GetDataForNonExistingKeyShouldReturnDefaultValue()
        {
            await _workflow.DoWorkflowTaskAsync(w => Assert.Equal(0, w.GetData<int>("Id")), forceExecution: true);
        }

        [Fact]
        public async Task SetDataForKeyShouldRemoveKeyIfValueIsDefault()
        {
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    w.SetData("BypassDates", true);
                    w.SetData("BypassDates", false);
                    Assert.False(w.Data.ContainsKey("BypassDates"));
                },
                forceExecution: true);
        }

        [Fact]
        public async Task SetTransientDataForDictionariesShouldOverrideExistingValuesAndIgnoreOthers()
        {
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    w.SetTransientData(new Dictionary<string, object> { ["Id"] = 1, ["BypassDates"] = true });
                    w.SetTransientData(new Dictionary<string, object> { ["Id"] = 2 });
                    Assert.Equal(2, w.TransientData["Id"]);
                    Assert.Equal(true, w.GetTransientData<bool>("BypassDates"));
                },
                forceExecution: true);
        }

        [Fact]
        public async Task GetTransientDataForNonExistingKeyShouldReturnDefaultValue()
        {
            await _workflow.DoWorkflowTaskAsync(
                w => Assert.Equal(0, w.GetTransientData<int>("Id")),
                forceExecution: true);
        }

        [Fact]
        public async Task DoWorkflowTaskShouldRestoreWorkflowCancellationToken()
        {
            var ct = Utilities.CurrentCancellationToken;
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    Assert.False(Utilities.CurrentCancellationToken.IsCancellationRequested);
                    Assert.NotEqual(ct, Utilities.CurrentCancellationToken);
                },
                forceExecution: true);
            Assert.Equal(ct, Utilities.CurrentCancellationToken);
        }

        [Fact]
        public async Task DoWorkflowTask2ShouldRestoreWorkflowCancellationToken()
        {
            var ct = Utilities.CurrentCancellationToken;
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    Assert.False(Utilities.CurrentCancellationToken.IsCancellationRequested);
                    Assert.NotEqual(ct, Utilities.CurrentCancellationToken);
                    return 1;
                },
                forceExecution: true);
            Assert.Equal(ct, Utilities.CurrentCancellationToken);
        }

        [Fact]
        public async Task DoWorkflowTaskShouldWaitUntilWorkflowIsStarted()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(100).ContinueWith(_ => _workflow.StartWorkflow());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    Assert.Equal(TaskStatus.RanToCompletion, _workflow.StartedTask.Status);
                });

            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task DoWorkflowTask2ShouldWaitUntilWorkflowIsStarted()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(100).ContinueWith(_ => _workflow.StartWorkflow());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    Assert.Equal(TaskStatus.RanToCompletion, _workflow.StartedTask.Status);
                    return 1;
                });

            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task DoWorkflowTaskShouldExecuteTaskEvenIfWorkflowIsInCacellationIfForceIsSpecified()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.CancelWorkflow();
            await workflow.DoWorkflowTaskAsync(_ => { }, true);
            await workflow.DoWorkflowTaskAsync(_ => 1, true);
        }

        [Fact]
        public async Task ConfiguredActionShouldBeExecutedOnRequest()
        {
            _workflow.ConfigureAction("Action 1", () => 2);
            _workflow.StartWorkflow();
            var res = await _workflow.ExecuteActionAsync<int>("Action 1");
            Assert.Equal(2, res);
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task ConfiguredActionHandlerShouldBeInvokedWithPassedParameters()
        {
            NamedValues invocationParameters = null;
            _workflow.ConfigureAction("Action 1", p => invocationParameters = p, synonym: "Action Synonym");
            _workflow.StartWorkflow();
            var parameters = new Dictionary<string, object> { ["Id"] = 1 };
            await _workflow.ExecuteActionAsync("Action Synonym", parameters);
            Assert.True(parameters.SequenceEqual(invocationParameters.Data));
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task NonConfiguredActionsShouldThrowAoore()
        {
            _workflow.ConfigureAction("Action 1", () => 2);
            _workflow.StartWorkflow();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => _workflow.ExecuteActionAsync("Action 2"));

            Assert.IsType(typeof(ArgumentOutOfRangeException), ex);
        }

        [Fact]
        public async Task IfActionNotAllowedAndThrowNotAllowedSpecifiedThenActionExecutionShouldThrowIoe()
        {
            _workflow.ConfigureAction("Action 1", () => 2);
            _workflow.StartWorkflow();
            _workflow.ActionsAllowed = false;

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => _workflow.ExecuteActionAsync("Action 1"));

            Assert.IsType(typeof(InvalidOperationException), ex);
        }

        [Fact]
        public async Task IfActionNotAllowedAndThrowNotAllowedDisabledThenActionExecutionShouldSkipAction()
        {
            _workflow.ConfigureAction("Action 1", () => 2);
            _workflow.StartWorkflow();
            _workflow.ActionsAllowed = false;
            await _workflow.ExecuteActionAsync("Action 1", throwNotAllowed: false);
            await _workflow.CompletedTask;
        }

        [Fact]
        public void ConfiguringTheSameActionMultipleTimesShouldThrowIoe()
        {
            _workflow.ConfigureAction("Action 1", () => 2);

            var ex = Record.Exception(() => _workflow.ConfigureAction("Action 1", () => 3));

            Assert.IsType(typeof(InvalidOperationException), ex);
        }

        [Fact]
        public async Task ActionCouldBeInvokedViaSynonym()
        {
            _workflow.ConfigureAction("Action 1", () => 3, synonyms: new[] { "Action First" });
            _workflow.StartWorkflow();
            var res = await _workflow.ExecuteActionAsync<int>("Action First");
            Assert.Equal(3, res);
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task ExecuteActionShouldWaitUntilWorkflowInitializationCompleted()
        {
            var workflow = new TestWorkflow(() => _workflowRepo, false);
            workflow.ConfigureAction("Action 1", () => 1);
            workflow.StartWorkflow();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(5).ContinueWith(_ => workflow.SetStateInitialized());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await workflow.ExecuteActionAsync<int>("Action 1");
            Assert.Equal(TaskStatus.RanToCompletion, workflow.StateInitializedTask.Status);
            await workflow.CompletedTask;
        }

        [Fact]
        public async Task GetAvailableActionsShouldReturnActionsInOrderOfConfiguringOfThem()
        {
            _workflow.ConfigureAction("Action 1", () => 1);
            _workflow.ConfigureAction("Action 2", () => 2);
            _workflow.ConfigureAction("Action 0", () => 0);
            _workflow.StartWorkflow();
            var res = await _workflow.GetAvailableActionsAsync();
            Assert.True(res.SequenceEqual(new[] { "Action 1", "Action 2", "Action 0" }));
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task GetAvailableActionsShouldSkipNotAllowedActions()
        {
            _workflow.ConfigureAction("Action 1", () => 1);
            _workflow.ConfigureAction("Action 2", () => 2);
            _workflow.ConfigureAction("Action 0", () => 0);
            _workflow.NotAllowedAction = "Action 2";
            _workflow.StartWorkflow();
            var res = await _workflow.GetAvailableActionsAsync();
            Assert.True(res.SequenceEqual(new[] { "Action 1", "Action 0" }));
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task GetAvailableActionsShouldNotReturnSynonyms()
        {
            _workflow.ConfigureAction("Action 1", () => 1, synonyms: new[] { "Action First" });
            _workflow.StartWorkflow();
            var res = await _workflow.GetAvailableActionsAsync();
            Assert.True(res.SequenceEqual(new[] { "Action 1" }));
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task GetAvailableActionsShouldNotReturnHiddenActions()
        {
            _workflow.ConfigureAction("Action 0", () => 0);
            _workflow.ConfigureAction("Action 1", () => 1, isHidden: true);
            _workflow.ConfigureAction("Action 2", () => 2);
            _workflow.StartWorkflow();

            var res = await _workflow.GetAvailableActionsAsync();

            Assert.True(res.SequenceEqual(new[] { "Action 0", "Action 2" }));
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task GetAvailableActionsShouldWaitUntilWorkflowInitializationCompleted()
        {
            var workflow = new TestWorkflow(() => _workflowRepo, false);
            workflow.ConfigureAction("Action 1", () => 1);
            workflow.StartWorkflow();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(5).ContinueWith(_ => workflow.SetStateInitialized());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await workflow.GetAvailableActionsAsync();
            Assert.Equal(TaskStatus.RanToCompletion, workflow.StateInitializedTask.Status);
            await workflow.CompletedTask;
        }

        [Fact]
        public void ActionMetadataIsAvailableInActionHandler()
        {
            _workflow.ConfigureAction(
                "Action 1",
                () => 0,
                metadata: new Dictionary<string, object> { ["Metadata1"] = "Something" });

            var metadata = _workflow.GetActionMetadata("Action 1");

            Assert.True(metadata.GetData<string>("Metadata1") == "Something");
        }

        [Fact]
        public async Task WhenActionExecutedSaveWorkflowDataShouldBeCalled()
        {
            var workflowRepo = new WorkflowRepository();
            var workflow = new TestWorkflow(() => workflowRepo, true);
            workflow.ConfigureAction("Action 1", () => 0);
            workflow.StartWorkflow();

            await workflow.StartedTask;
            Assert.Equal(1, workflowRepo.SaveWorkflowDataCounter);

            await workflow.ExecuteActionAsync("Action 1");

            Assert.Equal(2, workflowRepo.SaveWorkflowDataCounter);
            await workflow.CompletedTask;
        }

        [Fact]
        public async Task RunViaWorkflowTaskSchedulerShouldCompleteSyncIfRunFromWorkflowThread()
        {
            await _workflow.DoWorkflowTaskAsync(
                () =>
                {
                    var isRun = false;
                    Assert.Equal(Task.CompletedTask, _workflow.RunViaWorkflowTaskScheduler(() => { isRun = true; }));
                    Assert.True(isRun);
                },
                forceExecution: true);
        }

        [Fact]
        public async Task RunViaWorkflowTaskSchedulerShouldCompleteAsyncIfRunOutsideOfWorkflowThread()
        {
            var isRun = false;
            var task = _workflow.RunViaWorkflowTaskScheduler(() => { isRun = true; }, forceExecution: true);
            await task;
            Assert.NotEqual(Task.CompletedTask, task);
            Assert.True(isRun);
        }

        [Fact]
        public async Task RunViaWorkflowTaskScheduler2ShouldCompleteSyncIfRunFromWorkflowThread()
        {
            await _workflow.DoWorkflowTaskAsync(
                () =>
                {
                    var task = _workflow.RunViaWorkflowTaskScheduler(() => true);
                    Assert.Equal(TaskStatus.RanToCompletion, task.Status);
                    Assert.True(task.Result);
                },
                forceExecution: true);
        }

        [Fact]
        public async Task RunViaWorkflowTaskScheduler2ShouldCompleteASyncIfRunOutsideOfWorkflowThread()
        {
            var task = _workflow.RunViaWorkflowTaskScheduler(() => true, forceExecution: true);
            Assert.NotEqual(TaskStatus.RanToCompletion, task.Status);
            var res = await task;
            Assert.True(res);
        }

        [Fact]
        public async Task WhenActionExecutedItsCounterShouldBeIncremented()
        {
            _workflow.ConfigureAction("Action 1", _ => { });
            _workflow.StartWorkflow();

            Assert.Equal(false, _workflow.WasExecuted("Action 1"));
            Assert.Equal(0, _workflow.TimesExecuted("Action 1"));

            await _workflow.ExecuteActionAsync("Action 1");

            Assert.Equal(true, _workflow.WasExecuted("Action 1"));
            Assert.Equal(1, _workflow.TimesExecuted("Action 1"));

            await _workflow.ExecuteActionAsync("Action 1");

            Assert.Equal(true, _workflow.WasExecuted("Action 1"));
            Assert.Equal(2, _workflow.TimesExecuted("Action 1"));

            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task ClearTimesExecutedShouldRestActionCounter()
        {
            _workflow.ConfigureAction("Action 1", _ => { });
            _workflow.StartWorkflow();

            await _workflow.ExecuteActionAsync("Action 1");

            Assert.Equal(true, _workflow.WasExecuted("Action 1"));
            Assert.Equal(1, _workflow.TimesExecuted("Action 1"));

            _workflow.ClearTimesExecuted("Action 1");

            Assert.Equal(false, _workflow.WasExecuted("Action 1"));
            Assert.Equal(0, _workflow.TimesExecuted("Action 1"));

            await _workflow.CompletedTask;
        }

        private sealed class TestWorkflow : WorkflowBase
        {
            public TestWorkflow(
                Func<IWorkflowStateRepository> workflowRepoFactory)
                : base(workflowRepoFactory, false)
            {
                OnInit();
                SetStateInitialized();
            }

            public TestWorkflow(
                Func<IWorkflowStateRepository> workflowRepoFactory,
                bool isStateInitializedImmediatelyAfterStart)
                : base(workflowRepoFactory, isStateInitializedImmediatelyAfterStart)
            {
                OnInit();
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
                bool isHidden = false) =>
                    base.ConfigureAction(action, actionHandler, metadata, synonym, synonyms, isHidden);

            [SuppressMessage("ReSharper", "UnusedParameter.Local", Justification = "It is OK")]
            public new void ConfigureAction<T>(
                string action,
                Func<T> actionHandler,
                IReadOnlyDictionary<string, object> metadata = null,
                string synonym = null,
                IEnumerable<string> synonyms = null,
                bool isHidden = false) =>
                    base.ConfigureAction(action, actionHandler, metadata, synonym, synonyms, isHidden);

            // ReSharper disable once UnusedParameter.Local
            public new NamedValues GetActionMetadata(string action) => base.GetActionMetadata(action);

            protected override Task RunAsync()
            {
                return Task.Delay(20);
            }

            protected override bool IsActionAllowed(string action) => ActionsAllowed && action != NotAllowedAction;
        }

        private class WorkflowRepository : IWorkflowStateRepository
        {
            public int SaveWorkflowDataCounter { get; private set; }

            public void SaveWorkflowData(WorkflowBase workflow)
            {
                Assert.NotNull(workflow);
                ++SaveWorkflowDataCounter;
            }

            public void MarkWorkflowAsCompleted(WorkflowBase workflow)
            {
            }

            public void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception)
            {
                throw new NotImplementedException();
            }

            public void MarkWorkflowAsCanceled(WorkflowBase workflow)
            {
                throw new NotImplementedException();
            }

            public void MarkWorkflowAsSleeping(WorkflowBase workflow)
            {
                throw new NotImplementedException();
            }

            public void MarkWorkflowAsInProgress(WorkflowBase workflow)
            {
                throw new NotImplementedException();
            }
        }
    }
}
