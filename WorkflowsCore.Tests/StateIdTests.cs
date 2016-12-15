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

        private enum HiddenStates
        {
            State1
        }

        [Fact]
        public void AccessingIdForHiddenStateShouldThorwIoe()
        {
            StateId<States, HiddenStates> stateId = HiddenStates.State1;

            var ex = Record.Exception(() => stateId.Id);

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void AccessingHiddenIdForNonHiddenStateShouldThorwIoe()
        {
            StateId<States, HiddenStates> stateId = States.State1;

            var ex = Record.Exception(() => stateId.HiddenId);

            Assert.IsType<InvalidOperationException>(ex);
        }
    }
}
