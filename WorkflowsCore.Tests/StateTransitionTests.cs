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

            var transition = new StateTransition<States, string>(stateChild1Child1, new Disposable());

            Assert.Equal(new[] { state, stateChild1, stateChild1Child1 }, transition.Path.ToArray());
        }

        [Fact]
        public void FindPathFromShouldReturnPathAfterSpecifiedStateTillTarget()
        {
            var state = CreateState(States.State1);

            var stateChild1 = CreateState(States.State1Child1)
                .SubstateOf(state);

            var stateChild1Child1 = CreateState(States.State1Child1Child1)
                .SubstateOf(stateChild1);

            var transition = new StateTransition<States, string>(stateChild1Child1, new Disposable());

            Assert.Equal(new[] { stateChild1Child1 }, transition.FindPathFrom(stateChild1).ToArray());
        }

        [Fact]
        public void FindPathFromShouldReturnNullIfNoPathCouldBeFound()
        {
            var state = CreateState(States.State1);

            var stateChild1 = CreateState(States.State1Child1)
                .SubstateOf(state);

            var transition = new StateTransition<States, string>(stateChild1, new Disposable());

            Assert.Null(transition.FindPathFrom(CreateState(States.State2)));
        }

        [Fact]
        public void FindPathFromShouldReturnNullForReenterTransition()
        {
            var state = CreateState(States.State1);

            var transition = new StateTransition<States, string>(state, new Disposable());

            Assert.Null(transition.FindPathFrom(state));
        }

        [Fact]
        public void CompleteTransitionShouldInvokeCallbackAndDisposeOperation()
        {
            var state = CreateState(States.State1);

            var workflowOperation = new Disposable(true);
            State<States, string> newState = null;
            var transition = new StateTransition<States, string>(
                state,
                workflowOperation,
                onStateChangedHandler: s => newState = s);
            transition.CompleteTransition();

            Assert.True(workflowOperation.DisposeCalled);
            Assert.Same(state, newState);
        }

        private sealed class Disposable : IDisposable
        {
            private readonly bool _allowDispose;

            public Disposable(bool allowDispose = false)
            {
                _allowDispose = allowDispose;
            }

            public bool DisposeCalled { get; private set; }

            public void Dispose()
            {
                if (!_allowDispose)
                {
                    throw new NotImplementedException();
                }

                DisposeCalled = true;
            }
        }
    }
}
