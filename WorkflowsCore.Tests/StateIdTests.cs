using System;
using WorkflowsCore.StateMachines;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class StateIdTests
    {
        private enum States
        {
            State1
        }

        private enum InternalStates
        {
            State1
        }

        [Fact]
        public void AccessingIdForInternalStateShouldThorwIoe()
        {
            StateId<States, InternalStates> stateId = InternalStates.State1;

            var ex = Record.Exception(() => stateId.Id);

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void AccessingHiddenIdForNonInternalStateShouldThorwIoe()
        {
            StateId<States, InternalStates> stateId = States.State1;

            var ex = Record.Exception(() => stateId.InternalState);

            Assert.IsType<InvalidOperationException>(ex);
        }
    }
}
