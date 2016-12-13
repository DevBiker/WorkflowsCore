using System;
using System.Linq;
using WorkflowsCore.StateMachines;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class StateTransitionTests : BaseStateTest<StateTransitionTests.States>
    {
        public enum States
        {
            State1,
            State1Child1,
            State1Child1Child1,
            State2
        }

        [Fact]
        public void PathShouldBeInitializedToPathFromRootStateToTargetStateOfTransition()
        {
            var state = CreateState(States.State1);

            var stateChild1 = CreateState(States.State1Child1)
                .SubstateOf(state);

            var stateChild1Child1 = CreateState(States.State1Child1Child1)
                .SubstateOf(stateChild1);

            var trasition = new StateTransition<States>(stateChild1Child1, new Disposable());

            Assert.Equal(new[] { state, stateChild1, stateChild1Child1 }, trasition.Path.ToArray());
        }

        [Fact]
        public void FindPathFromShouldReturnPathAfterSpecifiedStateTillTarget()
        {
            var state = CreateState(States.State1);

            var stateChild1 = CreateState(States.State1Child1)
                .SubstateOf(state);

            var stateChild1Child1 = CreateState(States.State1Child1Child1)
                .SubstateOf(stateChild1);

            var trasition = new StateTransition<States>(stateChild1Child1, new Disposable());

            Assert.Equal(new[] { stateChild1Child1 }, trasition.FindPathFrom(stateChild1).ToArray());
        }

        [Fact]
        public void FindPathFromShouldReturnNullIfNoPathCouldBeFound()
        {
            var state = CreateState(States.State1);

            var stateChild1 = CreateState(States.State1Child1)
                .SubstateOf(state);

            var transition = new StateTransition<States>(stateChild1, new Disposable());

            Assert.Null(transition.FindPathFrom(CreateState(States.State2)));
        }

        [Fact]
        public void FindPathFromShouldReturnNullForReenterTransition()
        {
            var state = CreateState(States.State1);

            var transition = new StateTransition<States>(state, new Disposable());

            Assert.Null(transition.FindPathFrom(state));
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
