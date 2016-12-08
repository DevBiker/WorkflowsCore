using System;
using System.Linq;
using WorkflowsCore.StateMachines;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class StateTransitionTests
    {
        private enum States
        {
            State1,
            State1Child1,
            State1Child1Child1,
        }

        [Fact]
        public void PathShouldBeInitializedToPathFromRootStateToTargetStateOfTransition()
        {
            var state = new State<States>(States.State1);

            var stateChild1 = new State<States>(States.State1Child1)
                .SubstateOf(state);

            var stateChild1Child1 = new State<States>(States.State1Child1Child1)
                .SubstateOf(stateChild1);

            var trasition = new StateTransition<States>(stateChild1Child1, new Disposable());

            Assert.Equal(new[] { state, stateChild1, stateChild1Child1 }, trasition.Path.ToArray());
        }

        private sealed class Disposable : IDisposable
        {
            public void Dispose()
            {
                throw new NotImplementedException();
            }
        }
    }
}
