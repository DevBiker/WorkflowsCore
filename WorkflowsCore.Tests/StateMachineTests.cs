using WorkflowsCore.StateMachines;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class StateMachineTests
    {
        private readonly StateMachine<States> _stateMachine = new StateMachine<States>();

        public enum States
        {
            State1,
            State1Child1,
            State1Child2,
            State1Child2Child1,
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
    }
}
