using System;
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
        private readonly BaseStateTest<States> _baseStateTest = new BaseStateTest<States>(); 
        private readonly State<States, string> _state;

        public StateTests()
        {
            Workflow = new TestWorkflow();
            _state = CreateState(States.State1);
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

            CreateState(States.State1Child1)
                .SubstateOf(_state)
                .OnEnter().Do(() => Assert.True(false));

            CreateState(States.State1Child2)
                .SubstateOf(_state)
                .OnEnter().Do(() => Assert.Equal(1, counter++));

            var stateChild2Child1 = CreateState(States.State1Child2Child1)
                .SubstateOf(States.State1Child2)
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

            var state2 = CreateState(States.State2);

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

            var state2 = CreateState(States.State2);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(state2));

            await instance.Task;

            Assert.Equal(3, counter);
        }

        [Fact]
        public async Task WhenCompoundStateIsExitedOnExitHandlersShouldBeCalledStartingFromLeafToRoot()
        {
            var counter = 0;
            _state.OnExit().Do(() => Assert.Equal(2, counter++));

            CreateState(States.State1Child1)
                .SubstateOf(_state)
                .OnExit().Do(() => Assert.True(false));

            var stateChild2 = CreateState(States.State1Child2)
                .SubstateOf(_state)
                .OnExit().Do(() => Assert.Equal(1, counter++));

            var stateChild2Child1 = CreateState(States.State1Child2Child1)
                .SubstateOf(stateChild2)
                .OnExit().Do(() => Assert.Equal(0, counter++));

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(stateChild2Child1)));

            var state2 = CreateState(States.State2);

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

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WhenOnAsyncTaskCompletesItsGoToShouldBeExecuted()
        {
            StartWorkflow();

            var date = DateTime.Now.AddDays(3);
            _state.OnAsync(() => Workflow.WaitForDate(date)).GoTo(States.State2);

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

            var childState = CreateState(States.State1Child1).SubstateOf(_state);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(childState));

            await Workflow.ReadyTask;

            Assert.Same(childState, instance.Child.State);
            Assert.Equal(0, counter);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

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

            var childState = CreateState(States.State1Child1).SubstateOf(_state);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(childState)));

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(_state));

            await Workflow.ReadyTask;

            Assert.Null(instance.Child);
            Assert.Equal(0, counter);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToInnerByOnEnterHandlers()
        {
            StartWorkflow();

            var childState = CreateState(States.State1Child1).SubstateOf(_state);
            _state.OnEnter().GoTo(States.State1Child1);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            await Workflow.ReadyTask;

            Assert.Same(childState, instance.Child.State);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToItselfByOnEnterHandlers()
        {
            StartWorkflow();

            _state.OnEnter().GoTo(States.State1);

            var childState = CreateState(States.State1Child1).SubstateOf(_state);
            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(childState)));

            await Workflow.ReadyTask;

            Assert.Null(instance.Child);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

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
            var state2 = CreateState(States.State2);
            _state.OnEnter().GoTo(States.State2);

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

            var childState = CreateState(States.State1Child1).SubstateOf(_state);
            _state.OnExit().GoTo(States.State1Child1);

            var counter = 0;
            _state.OnExit().Do(() => ++counter);

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

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

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            Assert.Equal(0, enterCounter);
            Assert.Equal(1, activateCounter);

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task AllowedActionShouldBeReportedAsAllowed()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            _state.AllowActions("Action 1");

            var allowed = SetWorkflowTemporarily(Workflow, () => instance.IsActionAllowed("Action 1"));

            Assert.True(allowed);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task DisallowedActionShouldBeReportedAsDisallowed()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            _state.AllowActions("Action 1");
            _state.DisallowActions("Action 1");

            var allowed = SetWorkflowTemporarily(Workflow, () => instance.IsActionAllowed("Action 1"));

            Assert.False(allowed);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task UnknownActionShouldBeReportedAsUnknown()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            var allowed = SetWorkflowTemporarily(Workflow, () => instance.IsActionAllowed("Action 1"));

            Assert.Null(allowed);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task ChildStateOverridesActionAllowance()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var child = CreateState(States.State1Child1).SubstateOf(_state);
            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(child)));

            _state.AllowActions("Action 1");
            child.DisallowActions("Action 1");

            var allowed = SetWorkflowTemporarily(Workflow, () => instance.IsActionAllowed("Action 1"));

            Assert.False(allowed);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task OnAsyncShouldImportOperationOnRequest()
        {
            StartWorkflow();

            IDisposable operation = null;
            _state.OnAsync(
                () => Workflow.DoWorkflowTaskAsync(
                    async () =>
                    {
                        await Workflow.ReadyTask;

                        if (operation != null)
                        {
                            await Workflow.WaitForDate(DateTime.MaxValue);
                            return 1;
                        }

                        operation = await Workflow.WaitForReadyAndStartOperation();

#pragma warning disable 4014
                        Task.Delay(1).ContinueWith(_ => operation.Dispose());
#pragma warning restore 4014

                        return 1;
                    }).Unwrap(),
                getOperationForImport: () => operation)
                .Do(
                    async () =>
                    {
                        using (var operation2 = await Workflow.WaitForReadyAndStartOperation())
                        {
                            Assert.Same(operation, operation2);
                        }
                    });

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            await Workflow.ReadyTask;
            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await Workflow.StartedTask;
            await CancelWorkflowAsync();

            await instance.Task;
        }

        [Fact]
        public async Task OnAsync2ShouldImportOperationOnRequest()
        {
            StartWorkflow();

            IDisposable operation = null;
            _state.OnAsync(
                () => Workflow.DoWorkflowTaskAsync(
                    async () =>
                    {
                        if (operation != null)
                        {
                            await Workflow.WaitForDate(DateTime.MaxValue);
                            return;
                        }

                        operation = await Workflow.WaitForReadyAndStartOperation();

#pragma warning disable 4014
                        Task.Delay(1).ContinueWith(_ => operation.Dispose());
#pragma warning restore 4014
                    }).Unwrap(),
                getOperationForImport: () => operation)
                .Do(
                    async () =>
                    {
                        using (var operation2 = await Workflow.WaitForReadyAndStartOperation())
                        {
                            Assert.Same(operation, operation2);
                        }
                    });

            var instance = SetWorkflowTemporarily(Workflow, () => _state.Run(CreateTransition(_state)));

            await Workflow.ReadyTask;
            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await Workflow.StartedTask;
            await CancelWorkflowAsync();

            await instance.Task;
        }

        private State<States, string> CreateState(States state) => _baseStateTest.CreateState(state);

        private StateTransition<States, string> CreateTransition(State<States, string> state, bool isRestoring = false)
        {
            try
            {
                Workflow.CreateOperation();
                return new StateTransition<States, string>(state, Workflow.TryStartOperation(), isRestoring);
            }
            finally
            {
                Workflow.ResetOperation();
            }
        }

        public class TestWorkflow : WorkflowBase
        {
            public new void CreateOperation() => base.CreateOperation();

            public new IDisposable TryStartOperation() => base.TryStartOperation();

            public new void ResetOperation() => base.ResetOperation();

            protected override void OnActionsInit()
            {
                ConfigureAction("Action", synonym: "Action 1");
            }

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }
    }
}
