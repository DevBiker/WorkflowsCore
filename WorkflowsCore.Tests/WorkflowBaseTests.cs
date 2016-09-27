using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkflowsCore.Tests
{
    [TestClass]
    public class WorkflowBaseTests
    {
        private WorkflowRepository _workflowRepo;
        private TestWorkflow _workflow;

        [TestInitialize]
        public void TestInitialize()
        {
            _workflowRepo = new WorkflowRepository();
            _workflow = new TestWorkflow(() => _workflowRepo);
        }

        [TestMethod]
        public async Task StartedTaskShouldBeCompletedWhenWorkflowIsStarted()
        {
            Assert.AreNotEqual(TaskStatus.RanToCompletion, _workflow.StartedTask.Status);
            _workflow.StartWorkflow(initialWorkflowData: new Dictionary<string, object>());
            await _workflow.StartedTask;
            await _workflow.CompletedTask;
        }

        [TestMethod]
        public async Task StartedTaskShouldBeCompletedEvenIfStartingIsFailed()
        {
            Assert.AreNotEqual(TaskStatus.RanToCompletion, _workflow.StartedTask.Status);
            _workflow.StartWorkflow(
                initialWorkflowData: new Dictionary<string, object>(),
                beforeWorkflowStarted: () => { throw new InvalidOperationException(); });
            await Task.WhenAny(_workflow.StartedTask, Task.Delay(100));
            Assert.AreEqual(TaskStatus.RanToCompletion, _workflow.StartedTask.Status);
        }

        [TestMethod]
        [ExpectedException(typeof(TaskCanceledException))]
        public async Task IfWorkflowIsCanceledThenStartedTaskShouldBeCanceledIfItIsNotCompletedYet()
        {
            var workflow = new TestWorkflow(() => _workflowRepo, false);
            workflow.CancelWorkflow();
            await Task.WhenAny(workflow.StartedTask, Task.Delay(100)).Unwrap();
        }

        [TestMethod]
        [ExpectedException(typeof(TaskCanceledException))]
        public async Task IfWorkflowIsCanceledThenInitializedTaskShouldBeCanceledIfItIsNotCompletedYet()
        {
            var workflow = new TestWorkflow(() => _workflowRepo, false);
            workflow.CancelWorkflow();
            await Task.WhenAny(workflow.StateInitializedTask, Task.Delay(100)).Unwrap();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task WorkflowIdCouldBeSetOnlyOnce()
        {
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    _workflow.Id = 1;
                    Assert.AreEqual(1, _workflow.Id);
                    _workflow.Id = 2;
                },
                forceExecution: true);
        }

        [TestMethod]
        public async Task GetIdShouldReturnWorkflowId()
        {
            _workflow.StartWorkflow();
            await _workflow.DoWorkflowTaskAsync(w => _workflow.Id = 1);
            var id = await _workflow.GetIdAsync();
            Assert.AreEqual(1, (int)id);
            await _workflow.CompletedTask;
        }

        [TestMethod]
        public async Task SetDataForDictionariesShouldOverrideExistingValuesAndIgnoreOthers()
        {
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    w.SetData(new Dictionary<string, object> { ["Id"] = 1, ["BypassDates"] = true });
                    w.SetData(new Dictionary<string, object> { ["Id"] = 2 });
                    Assert.AreEqual(2, w.Data["Id"]);
                    Assert.AreEqual(true, w.GetData<bool>("BypassDates"));
                },
                forceExecution: true);
        }

        [TestMethod]
        public async Task GetDataForNonExistingKeyShouldReturnDefaultValue()
        {
            await _workflow.DoWorkflowTaskAsync(w => Assert.AreEqual(0, w.GetData<int>("Id")), forceExecution: true);
        }

        [TestMethod]
        public async Task SetDataForKeyShouldRemoveKeyIfValueIsDefault()
        {
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    w.SetData("BypassDates", true);
                    w.SetData("BypassDates", false);
                    Assert.IsFalse(w.Data.ContainsKey("BypassDates"));
                },
                forceExecution: true);
        }

        [TestMethod]
        public async Task SetTransientDataForDictionariesShouldOverrideExistingValuesAndIgnoreOthers()
        {
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    w.SetTransientData(new Dictionary<string, object> { ["Id"] = 1, ["BypassDates"] = true });
                    w.SetTransientData(new Dictionary<string, object> { ["Id"] = 2 });
                    Assert.AreEqual(2, w.TransientData["Id"]);
                    Assert.AreEqual(true, w.GetTransientData<bool>("BypassDates"));
                },
                forceExecution: true);
        }

        [TestMethod]
        public async Task GetTransientDataForNonExistingKeyShouldReturnDefaultValue()
        {
            await _workflow.DoWorkflowTaskAsync(
                w => Assert.AreEqual(0, w.GetTransientData<int>("Id")),
                forceExecution: true);
        }

        [TestMethod]
        public async Task DoWorkflowTaskShouldRestoreWorkflowCancellationToken()
        {
            var ct = Utilities.CurrentCancellationToken;
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    Assert.IsFalse(Utilities.CurrentCancellationToken.IsCancellationRequested);
                    Assert.AreNotEqual(ct, Utilities.CurrentCancellationToken);
                },
                forceExecution: true);
            Assert.AreEqual(ct, Utilities.CurrentCancellationToken);
        }

        [TestMethod]
        public async Task DoWorkflowTask2ShouldRestoreWorkflowCancellationToken()
        {
            var ct = Utilities.CurrentCancellationToken;
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    Assert.IsFalse(Utilities.CurrentCancellationToken.IsCancellationRequested);
                    Assert.AreNotEqual(ct, Utilities.CurrentCancellationToken);
                    return 1;
                },
                forceExecution: true);
            Assert.AreEqual(ct, Utilities.CurrentCancellationToken);
        }

        [TestMethod]
        public async Task DoWorkflowTaskShouldWaitUntilWorkflowIsStarted()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(100).ContinueWith(_ => _workflow.StartWorkflow());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    Assert.AreEqual(TaskStatus.RanToCompletion, _workflow.StartedTask.Status);
                });

            await _workflow.CompletedTask;
        }

        [TestMethod]
        public async Task DoWorkflowTask2ShouldWaitUntilWorkflowIsStarted()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(100).ContinueWith(_ => _workflow.StartWorkflow());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await _workflow.DoWorkflowTaskAsync(
                w =>
                {
                    Assert.AreEqual(TaskStatus.RanToCompletion, _workflow.StartedTask.Status);
                    return 1;
                });

            await _workflow.CompletedTask;
        }

        [TestMethod]
        public async Task DoWorkflowTaskShouldExecuteTaskEvenIfWorkflowIsInCacellationIfForceIsSpecified()
        {
            var cts = new CancellationTokenSource();
            var workflow = new TestWorkflow(() => _workflowRepo, cts.Token);
            cts.Cancel();
            await workflow.DoWorkflowTaskAsync(_ => { }, true);
            await workflow.DoWorkflowTaskAsync(_ => 1, true);
        }

        [TestMethod]
        public async Task ConfiguredActionShouldBeExecutedOnRequest()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 2);
            workflow.StartWorkflow();
            var res = await workflow.ExecuteActionAsync<int>("Action 1");
            Assert.AreEqual(2, res);
            await workflow.CompletedTask;
        }

        [TestMethod]
        public async Task ConfiguredActionHandlerShouldBeInvokedWithPassedParameters()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            NamedValues invocationParameters = null;
            workflow.ConfigureAction("Action 1", p => invocationParameters = p, synonym: "Action Synonym");
            workflow.StartWorkflow();
            var parameters = new Dictionary<string, object> { ["Id"] = 1 };
            await workflow.ExecuteActionAsync("Action Synonym", parameters);
            Assert.IsTrue(parameters.SequenceEqual(invocationParameters.Data));
            await workflow.CompletedTask;
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public async Task NonConfiguredActionsShouldThrowAoore()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 2);
            workflow.StartWorkflow();
            await workflow.ExecuteActionAsync("Action 2");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task IfActionNotAllowedAndThrowNotAllowedSpecifiedThenActionExecutionShouldThrowIoe()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 2);
            workflow.StartWorkflow();
            workflow.ActionsAllowed = false;
            await workflow.ExecuteActionAsync("Action 1");
        }

        [TestMethod]
        public async Task IfActionNotAllowedAndThrowNotAllowedDisabledThenActionExecutionShouldSkipAction()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 2);
            workflow.StartWorkflow();
            workflow.ActionsAllowed = false;
            await workflow.ExecuteActionAsync("Action 1", throwNotAllowed: false);
            await workflow.CompletedTask;
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ConfiguringTheSameActionMultipleTimesShouldThrowIoe()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 2);
            workflow.ConfigureAction("Action 1", () => 3);
        }

        [TestMethod]
        public async Task ActionCouldBeInvokedViaSynonym()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 3, synonyms: new[] { "Action First" });
            workflow.StartWorkflow();
            var res = await workflow.ExecuteActionAsync<int>("Action First");
            Assert.AreEqual(3, res);
            await workflow.CompletedTask;
        }

        [TestMethod]
        public async Task ExecuteActionShouldWaitUntilWorkflowInitializationCompleted()
        {
            var workflow = new TestWorkflow(() => _workflowRepo, false);
            workflow.ConfigureAction("Action 1", () => 1);
            workflow.StartWorkflow();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(5).ContinueWith(_ => workflow.SetStateInitialized());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await workflow.ExecuteActionAsync<int>("Action 1");
            Assert.AreEqual(TaskStatus.RanToCompletion, workflow.StateInitializedTask.Status);
            await workflow.CompletedTask;
        }

        [TestMethod]
        public async Task GetAvailableActionsShouldReturnActionsInOrderOfConfiguringOfThem()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 1);
            workflow.ConfigureAction("Action 2", () => 2);
            workflow.ConfigureAction("Action 0", () => 0);
            workflow.StartWorkflow();
            var res = await workflow.GetAvailableActionsAsync();
            Assert.IsTrue(res.SequenceEqual(new[] { "Action 1", "Action 2", "Action 0" }));
            await workflow.CompletedTask;
        }

        [TestMethod]
        public async Task GetAvailableActionsShouldSkipNotAllowedActions()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 1);
            workflow.ConfigureAction("Action 2", () => 2);
            workflow.ConfigureAction("Action 0", () => 0);
            workflow.NotAllowedAction = "Action 2";
            workflow.StartWorkflow();
            var res = await workflow.GetAvailableActionsAsync();
            Assert.IsTrue(res.SequenceEqual(new[] { "Action 1", "Action 0" }));
            await workflow.CompletedTask;
        }

        [TestMethod]
        public async Task GetAvailableActionsShouldNotReturnSynonyms()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 1, synonyms: new[] { "Action First" });
            workflow.StartWorkflow();
            var res = await workflow.GetAvailableActionsAsync();
            Assert.IsTrue(res.SequenceEqual(new[] { "Action 1" }));
            await workflow.CompletedTask;
        }

        [TestMethod]
        public async Task GetAvailableActionsShouldNotReturnHiddenActions()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 0", () => 0);
            workflow.ConfigureAction("Action 1", () => 1, isHidden: true);
            workflow.ConfigureAction("Action 2", () => 2);
            workflow.StartWorkflow();

            var res = await workflow.GetAvailableActionsAsync();

            Assert.IsTrue(res.SequenceEqual(new[] { "Action 0", "Action 2" }));
            await workflow.CompletedTask;
        }

        [TestMethod]
        public async Task GetAvailableActionsShouldWaitUntilWorkflowInitializationCompleted()
        {
            var workflow = new TestWorkflow(() => _workflowRepo, false);
            workflow.ConfigureAction("Action 1", () => 1);
            workflow.StartWorkflow();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(5).ContinueWith(_ => workflow.SetStateInitialized());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await workflow.GetAvailableActionsAsync();
            Assert.AreEqual(TaskStatus.RanToCompletion, workflow.StateInitializedTask.Status);
            await workflow.CompletedTask;
        }

        [TestMethod]
        public void ActionMetadataIsAvailableInActionHandler()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction(
                "Action 1",
                () => 0,
                metadata: new Dictionary<string, object> { ["Metadata1"] = "Something" });

            var metadata = workflow.GetActionMetadata("Action 1");

            Assert.IsTrue(metadata.GetData<string>("Metadata1") == "Something");
        }

        [TestMethod]
        public async Task WhenActionExecutedSaveWorkflowDataShouldBeCalled()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", () => 0);
            workflow.StartWorkflow();

            await workflow.StartedTask;
            Assert.AreEqual(1, _workflowRepo.SaveWorkflowDataCounter);

            await workflow.ExecuteActionAsync("Action 1");

            Assert.AreEqual(2, _workflowRepo.SaveWorkflowDataCounter);
            await workflow.CompletedTask;
        }

        [TestMethod]
        public async Task RunViaWorkflowTaskSchedulerShouldCompleteSyncIfRunFromWorkflowThread()
        {
            await _workflow.DoWorkflowTaskAsync(
                () =>
                {
                    var isRun = false;
                    Assert.AreEqual(Task.CompletedTask, _workflow.RunViaWorkflowTaskScheduler(() => { isRun = true; }));
                    Assert.IsTrue(isRun);
                },
                forceExecution: true);
        }

        [TestMethod]
        public async Task RunViaWorkflowTaskSchedulerShouldCompleteAsyncIfRunOutsideOfWorkflowThread()
        {
            var isRun = false;
            var task = _workflow.RunViaWorkflowTaskScheduler(() => { isRun = true; }, forceExecution: true);
            await task;
            Assert.AreNotEqual(Task.CompletedTask, task);
            Assert.IsTrue(isRun);
        }

        [TestMethod]
        public async Task RunViaWorkflowTaskScheduler2ShouldCompleteSyncIfRunFromWorkflowThread()
        {
            await _workflow.DoWorkflowTaskAsync(
                () =>
                {
                    var task = _workflow.RunViaWorkflowTaskScheduler(() => true);
                    Assert.AreEqual(TaskStatus.RanToCompletion, task.Status);
                    Assert.IsTrue(task.Result);
                },
                forceExecution: true);
        }

        [TestMethod]
        public async Task RunViaWorkflowTaskScheduler2ShouldCompleteASyncIfRunOutsideOfWorkflowThread()
        {
            var task = _workflow.RunViaWorkflowTaskScheduler(() => true, forceExecution: true);
            Assert.AreNotEqual(TaskStatus.RanToCompletion, task.Status);
            var res = await task;
            Assert.IsTrue(res);
        }

        [TestMethod]
        public async Task WhenActionExecutedItsCounterShouldBeIncremented()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", _ => { });
            workflow.StartWorkflow();

            Assert.AreEqual(false, workflow.WasExecuted("Action 1"));
            Assert.AreEqual(0, workflow.TimesExecuted("Action 1"));

            await workflow.ExecuteActionAsync("Action 1");

            Assert.AreEqual(true, workflow.WasExecuted("Action 1"));
            Assert.AreEqual(1, workflow.TimesExecuted("Action 1"));

            await workflow.ExecuteActionAsync("Action 1");

            Assert.AreEqual(true, workflow.WasExecuted("Action 1"));
            Assert.AreEqual(2, workflow.TimesExecuted("Action 1"));

            await workflow.CompletedTask;
        }

        [TestMethod]
        public async Task ClearTimesExecutedShouldRestActionCounter()
        {
            var workflow = new TestWorkflow(() => _workflowRepo);
            workflow.ConfigureAction("Action 1", _ => { });
            workflow.StartWorkflow();

            await workflow.ExecuteActionAsync("Action 1");

            Assert.AreEqual(true, workflow.WasExecuted("Action 1"));
            Assert.AreEqual(1, workflow.TimesExecuted("Action 1"));

            workflow.ClearTimesExecuted("Action 1");

            Assert.AreEqual(false, workflow.WasExecuted("Action 1"));
            Assert.AreEqual(0, workflow.TimesExecuted("Action 1"));

            await workflow.CompletedTask;
        }

        private sealed class TestWorkflow : WorkflowBase
        {
            public TestWorkflow(
                Func<IWorkflowStateRepository> workflowRepoFactory,
                CancellationToken parentCancellationToken = default(CancellationToken))
                : base(workflowRepoFactory, false, parentCancellationToken)
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
                Assert.IsNotNull(workflow);
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
