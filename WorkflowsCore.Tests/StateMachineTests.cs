using System;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.StateMachines;
using WorkflowsCore.Time;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class StateMachineTests : BaseWorkflowTest<StateMachineTests.TestWorkflow>
    {
        private readonly StateMachine<States> _stateMachine = new StateMachine<States>();

        public enum States
        {
            State1,
            State2
        }

        [Fact]
        public void ConfigureStateShouldCreateStateIfItDoesNotExist()
        {
            var state = _stateMachine.ConfigureState(States.State1);
            var state2 = _stateMachine.ConfigureState(States.State1);

            Assert.NotNull(state);
            Assert.Same(state, state2);
        }

        [Fact]
        public void ConfigureHiddenStateShouldCreateHiddenStateIfItDoesNotExist()
        {
            var state = _stateMachine.ConfigureHiddenState("Hidden State 1");
            var state2 = _stateMachine.ConfigureHiddenState("Hidden State 1");

            Assert.NotNull(state);
            Assert.Same(state, state2);
        }

        [Fact]
        public async Task RunShouldStartInitialState()
        {
            var state = _stateMachine.ConfigureState(States.State1);

            Workflow = new TestWorkflow();
            StartWorkflow();

            State<States, string> curState = null;
            var t = await Workflow.DoWorkflowTaskAsync(w => _stateMachine.Run(w, States.State1, true, s => curState = s));

            await Workflow.ReadyTask;

            Assert.Same(state, curState);

            await CancelWorkflowAsync();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => t);

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task RunShouldHandleSubsequentTransitions()
        {
            var date = DateTime.Now.AddDays(3);
            var state2 = _stateMachine.ConfigureState(States.State2);
            _stateMachine.ConfigureState(States.State1)
                .OnAsync(() => Workflow.WaitForDate(date)).GoTo(state2);

            Utilities.TimeProvider = new TestingTimeProvider();
            Workflow = new TestWorkflow();
            StartWorkflow();

            State<States, string> curState = null;
            var t = await Workflow.DoWorkflowTaskAsync(w => _stateMachine.Run(w, States.State1, false, s => curState = s));

            await Workflow.ReadyTask;

            TestingTimeProvider.Current.SetCurrentTime(date);

            await Workflow.DoWorkflowTaskAsync(async () => await Task.Delay(1)).Unwrap();
            await Workflow.ReadyTask;

            Assert.Same(state2, curState);

            await CancelWorkflowAsync();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => t);

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task RunShouldShouldThrowAoreIfInitialStateWasNotConfigured()
        {
            var t = _stateMachine.Run(new TestWorkflow(), States.State1, true);

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => t);

            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        public class TestWorkflow : WorkflowBase
        {
            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }
    }
}
