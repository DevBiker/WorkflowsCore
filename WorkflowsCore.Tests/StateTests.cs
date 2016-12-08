﻿using System;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.StateMachines;
using WorkflowsCore.Time;
using Xunit;
using static WorkflowsCore.StateMachines.StateExtensions;

namespace WorkflowsCore.Tests
{
    public class StateTests : BaseWorkflowTest<StateTests.TestWorkflow>
    {
        private State<States> _state;

        public StateTests()
        {
            _state = new State<States>(States.State1);
            Workflow = new TestWorkflow();
        }

        public enum States
        {
            State1,
            State1Child1,
            State1Child2,
            State1Child2Child1,
            State2
        }

        [Fact]
        public async Task WhenStateIsEnteredOnEnterHandlersShouldBeCalledInOrderOfDeclaration()
        {
            var counter = 0;
            _state
                .OnEnter().Do(() => Assert.Equal(0, counter++))
                .OnEnter().Do(() => Assert.Equal(1, counter++))
                .OnEnter().Do(() => Assert.Equal(2, counter++));

            var cts = new CancellationTokenSource();
            var instance = Utilities.SetCurrentCancellationTokenTemporarily(
                cts.Token,
                () => SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state))));

            cts.Cancel();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => instance.Task);

            Assert.IsType<TaskCanceledException>(ex);
            Assert.Equal(3, counter);
        }

        [Fact]
        public async Task WhenCompoundStateIsEnteredOnEnterHandlersShouldBeCalledStartingFromRootToDescendants()
        {
            var counter = 0;
            _state.OnEnter().Do(() => Assert.Equal(0, counter++));

            new State<States>(States.State1Child1)
                .SubstateOf(_state)
                .OnEnter().Do(() => Assert.True(false));

            var stateChild2 = new State<States>(States.State1Child2)
                .SubstateOf(_state)
                .OnEnter().Do(() => Assert.Equal(1, counter++));

            var stateChild2Child1 = new State<States>(States.State1Child2Child1)
                .SubstateOf(stateChild2)
                .OnEnter().Do(() => Assert.Equal(2, counter++));

            var cts = new CancellationTokenSource();
            var instance = Utilities.SetCurrentCancellationTokenTemporarily(
                cts.Token,
                () => SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(stateChild2Child1))));

            cts.Cancel();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => instance.Task);

            Assert.IsType<TaskCanceledException>(ex);
            Assert.Equal(3, counter);
        }

        [Fact]
        public async Task WhenTransitionToNonChildStateInitiatedStateInstanceShouldBeStopped()
        {
            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            var state2 = new State<States>(States.State2);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(state2));

            await instance.Task;
        }

        [Fact]
        public async Task WhenStateIsExitedOnExitHandlersShouldBeCalledInOrderOfDeclaration()
        {
            var counter = 0;
            _state
                .OnExit().Do(() => Assert.Equal(0, counter++))
                .OnExit().Do(() => Assert.Equal(1, counter++))
                .OnExit().Do(() => Assert.Equal(2, counter++));

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            var state2 = new State<States>(States.State2);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(state2));

            await instance.Task;

            Assert.Equal(3, counter);
        }

        [Fact]
        public async Task WhenCompoundStateIsExitedOnExitHandlersShouldBeCalledStartingFromLeafToRoot()
        {
            var counter = 0;
            _state.OnExit().Do(() => Assert.Equal(2, counter++));

            new State<States>(States.State1Child1)
                .SubstateOf(_state)
                .OnExit().Do(() => Assert.True(false));

            var stateChild2 = new State<States>(States.State1Child2)
                .SubstateOf(_state)
                .OnExit().Do(() => Assert.Equal(1, counter++));

            var stateChild2Child1 = new State<States>(States.State1Child2Child1)
                .SubstateOf(stateChild2)
                .OnExit().Do(() => Assert.Equal(0, counter++));

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(stateChild2Child1)));

            var state2 = new State<States>(States.State2);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(state2));

            await instance.Task;

            Assert.Equal(3, counter);
        }

        [Fact]
        public async Task WhenOnAsyncTaskCompletesItsDoHandlerShouldBeExecuted()
        {
            StartWorkflow();

            var date = DateTime.Now.AddDays(3);
            var tcs = new TaskCompletionSource<bool>();
            _state
                .OnAsync(
                    async () =>
                    {
                        await Workflow.WaitForDate(date);
                        return date;
                    })
                .Do(
                    d =>
                    {
                        Assert.Equal(date, d);
                        date = d.AddDays(1);

                        // ReSharper disable once AccessToModifiedClosure
                        tcs.SetResult(true);
                    });

            Utilities.TimeProvider = new TestingTimeProvider();

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            TestingTimeProvider.Current.SetCurrentTime(date);

            await tcs.Task;

            tcs = new TaskCompletionSource<bool>();

            TestingTimeProvider.Current.SetCurrentTime(date);

            await tcs.Task;

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(new State<States>(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WhenOnAsyncTaskCompletesItsGoToShouldBeExecuted()
        {
            StartWorkflow();

            var date = DateTime.Now.AddDays(3);
            _state.OnAsync(() => Workflow.WaitForDate(date)).GoTo(new State<States>(States.State2));

            Utilities.TimeProvider = new TestingTimeProvider();

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            await Workflow.ReadyTask;

            TestingTimeProvider.Current.SetCurrentTime(date);

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TransitionToInnerStateShouldNotExecuteExitHandlersForParentState()
        {
            StartWorkflow();

            var counter = 0;
            _state.OnExit().Do(() => ++counter);

            var childState = new State<States>(States.State1Child1).SubstateOf(_state);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(childState));

            await Workflow.ReadyTask;

            Assert.Same(childState, instance.Child.State);
            Assert.Equal(0, counter);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(new State<States>(States.State2)));

            await instance.Task;

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TransitionFromInnerStateShouldNotExecuteExitHandlersForParentState()
        {
            StartWorkflow();

            var counter = 0;
            _state.OnExit().Do(() => ++counter);

            var childState = new State<States>(States.State1Child1).SubstateOf(_state);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(childState)));

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(_state));

            await Workflow.ReadyTask;

            Assert.Null(instance.Child);
            Assert.Equal(0, counter);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(new State<States>(States.State2)));

            await instance.Task;

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToInnerByOnEnterHandlers()
        {
            StartWorkflow();

            var childState = new State<States>(States.State1Child1).SubstateOf(_state);
            _state.OnEnter().GoTo(childState);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            await Workflow.ReadyTask;

            Assert.Same(childState, instance.Child.State);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(new State<States>(States.State2)));

            await instance.Task;

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToItselfByOnEnterHandlers()
        {
            StartWorkflow();

            _state.OnEnter().GoTo(_state);

            var childState = new State<States>(States.State1Child1).SubstateOf(_state);
            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(childState)));

            await Workflow.ReadyTask;

            Assert.Null(instance.Child);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(new State<States>(States.State2)));

            await instance.Task;

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToSiblingByOnEnterHandlers()
        {
            StartWorkflow();

            var counter = 0;
            _state.OnExit().Do(() => ++counter);
            var state2 = new State<States>(States.State2);
            _state.OnEnter().GoTo(state2);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            var transition = await instance.Task;

            Assert.Same(state2, transition.State);
            Assert.Equal(0, counter);

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToOtherByOnExitHandlers()
        {
            StartWorkflow();

            var childState = new State<States>(States.State1Child1).SubstateOf(_state);
            _state.OnExit().GoTo(childState);

            var counter = 0;
            _state.OnExit().Do(() => ++counter);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(new State<States>(States.State2)));

            var transition = await instance.Task;
            Assert.Same(childState, transition.State);
            Assert.Equal(1, counter);

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task OnActivateHandlersShouldBeCalledDuringStateRestoring()
        {
            StartWorkflow();

            var enterCounter = 0;
            _state.OnEnter().Do(() => ++enterCounter);

            var activateCounter = 0;
            _state.OnActivate().Do(() => ++activateCounter);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state, true)));

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(new State<States>(States.State2)));

            await instance.Task;

            Assert.Equal(0, enterCounter);
            Assert.Equal(1, activateCounter);

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        private StateTransition<States> CreateTransition(State<States> state, bool isRestoring = false)
        {
            Workflow.CreateOperation();
            return new StateTransition<States>(state, Workflow.TryStartOperation(), isRestoring);
        }

        public class TestWorkflow : WorkflowBase
        {
            public new void CreateOperation() => base.CreateOperation();

            public new IDisposable TryStartOperation() => base.TryStartOperation();

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }
    }
}
