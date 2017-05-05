using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.StateMachines;
using WorkflowsCore.Time;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class StateExtensionsTests : BaseWorkflowTest<StateExtensionsTests.TestWorkflow>
    {
        private readonly StateMachine<States> _stateMachine = new StateMachine<States>();

        private enum States
        {
            State1
        }

        [Fact]
        public async Task OnDateShouldExecuteChainOnSpecifiedDate()
        {
            var date = DateTime.Now.AddDays(3);
            var tcs = new TaskCompletionSource<DateTime>();
            var state = _stateMachine.ConfigureState(States.State1).OnDate(() => date).Do(d => tcs.SetResult(d));

            Utilities.TimeProvider = new TestingTimeProvider();

            Workflow = new TestWorkflow();
            StartWorkflow();

            var t = Workflow.WaitForAny(
                () => tcs.Task,
                () => Workflow.DoWorkflowTaskAsync(w => _stateMachine.Run(w, state.StateId, false).Task));

            TestingTimeProvider.Current.SetCurrentTime(date);

            await t;

            Assert.Equal(date, tcs.Task.Result);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task OnActionShouldExecuteChainWhenSpecifiedActionExecuted()
        {
            var tcs = new TaskCompletionSource<NamedValues>();
            var state = _stateMachine.ConfigureState(States.State1).OnAction("Action 1").Do(v => tcs.SetResult(v));

            Workflow = new TestWorkflow();
            StartWorkflow();

            var t = Workflow.WaitForAny(
                () => tcs.Task,
                () => Workflow.DoWorkflowTaskAsync(w => _stateMachine.Run(w, state.StateId, false).Task));

            await Workflow.ReadyTask;

            var parameters = new Dictionary<string, object>
            {
                ["Id"] = 1
            };
            await Workflow.ExecuteActionAsync("Action 1", parameters);

            await t;

            tcs.Task.Result.SetDataField("Action", (string)null);
            tcs.Task.Result.SetDataField("ActionOperation", (IDisposable)null);
            Assert.Equal(parameters.ToList(), tcs.Task.Result.Data.ToList());

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task OnActionsShouldExecuteChainWhenOneOfSpecifiedActionsExecuted()
        {
            var tcs = new TaskCompletionSource<NamedValues>();
            var state = _stateMachine.ConfigureState(States.State1)
                .OnActions("Some description", false, "Action 1", "Action 2")
                .Do(v => tcs.SetResult(v));

            Workflow = new TestWorkflow();
            StartWorkflow();

            var t = Workflow.WaitForAny(
                () => tcs.Task,
                () => Workflow.DoWorkflowTaskAsync(w => _stateMachine.Run(w, state.StateId, false).Task));

            await Workflow.ReadyTask;

            var parameters = new Dictionary<string, object> { ["Id"] = 1 };
            await Workflow.ExecuteActionAsync("Action 2", parameters);

            await t;

            tcs.Task.Result.SetDataField("Action", (string)null);
            tcs.Task.Result.SetDataField("ActionOperation", (IDisposable)null);
            Assert.Equal(parameters.ToList(), tcs.Task.Result.Data.ToList());

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task OnActionWithWasExecutedCheckShouldExecuteChainWhenSpecifiedActionExecuted()
        {
            var tcs = new TaskCompletionSource<bool>();
            var state = _stateMachine.ConfigureState(States.State1)
                .OnActionWithWasExecutedCheck("Action 1").Do(() => tcs.SetResult(true));

            Workflow = new TestWorkflow();
            StartWorkflow();

            var t = Workflow.WaitForAny(
                () => tcs.Task,
                () => Workflow.DoWorkflowTaskAsync(w => _stateMachine.Run(w, state.StateId, false).Task));

            await Workflow.ReadyTask;

            await Workflow.ExecuteActionAsync("Action 1");

            await t;

            Assert.Equal(TaskStatus.RanToCompletion, tcs.Task.Status);

            await CancelWorkflowAsync();
        }

        public class TestWorkflow : WorkflowBase
        {
            protected override void OnActionsInit()
            {
                base.OnActionsInit();

                ConfigureAction("Action 1");
                ConfigureAction("Action 2");
            }

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }
    }
}
