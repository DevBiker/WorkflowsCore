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
            StartWorkflow();
            await Workflow.ReadyTask;

            var counter = 0;
            _state
                .OnEnter().Do(() => Assert.Equal(0, counter++))
                .OnEnter().Do(() => Assert.Equal(1, counter++))
                .OnEnter().Do(() => Assert.Equal(2, counter++));

            var cts = new CancellationTokenSource();
            var instance = Utilities.SetCurrentCancellationTokenTemporarily(cts.Token, () => RunState(_state));

            cts.Cancel();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => instance.Task);

            Assert.IsType<TaskCanceledException>(ex);
            Assert.Equal(3, counter);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WhenCompoundStateIsEnteredOnEnterHandlersShouldBeCalledStartingFromRootToDescendants()
        {
            StartWorkflow();
            await Workflow.ReadyTask;

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
                () => RunState(stateChild2Child1));

            cts.Cancel();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => instance.Task);

            Assert.IsType<TaskCanceledException>(ex);
            Assert.Equal(3, counter);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WhenTransitionToNonChildStateInitiatedStateInstanceShouldBeStopped()
        {
            StartWorkflow();
            await Workflow.ReadyTask;

            var instance = RunState(_state);

            var state2 = CreateState(States.State2);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(state2));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WhenStateIsExitedOnExitHandlersShouldBeCalledInOrderOfDeclaration()
        {
            StartWorkflow();
            await Workflow.ReadyTask;

            var counter = 0;
            _state
                .OnExit().Do(() => Assert.Equal(0, counter++))
                .OnExit().Do(() => Assert.Equal(1, counter++))
                .OnExit().Do(() => Assert.Equal(2, counter++));

            var instance = RunState(_state);

            var state2 = CreateState(States.State2);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(state2));

            await instance.Task;

            Assert.Equal(3, counter);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WhenStateIsExitedWorkflowOperationShouldBeImported()
        {
            StartWorkflow();
            await Workflow.ReadyTask;

            _state
                .OnExit().Do(
                    () =>
                    {
                        Workflow.CreateOperation();
                        var operation = Workflow.TryStartOperation();
                        Assert.NotNull(operation);
                        operation.Dispose();
                    });

            var instance = RunState(_state);

            var state2 = CreateState(States.State2);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(state2));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WhenCompoundStateIsExitedOnExitHandlersShouldBeCalledStartingFromLeafToRoot()
        {
            StartWorkflow();
            await Workflow.ReadyTask;

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

            var instance = RunState(stateChild2Child1);

            var state2 = CreateState(States.State2);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(state2));

            await instance.Task;

            Assert.Equal(3, counter);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WhenOnAsyncTaskCompletesItsDoHandlerShouldBeExecuted()
        {
            StartWorkflow();
            await Workflow.ReadyTask;

            var date = DateTime.Now.AddDays(3);
            var tcs = new TaskCompletionSource<bool>();
            _state
                .OnAsync(
                    async () =>
                    {
                        Workflow.CreateOperation();
                        var operation = Workflow.TryStartOperation();
                        Assert.Null(operation); // OnAsyncs() should not import current workflow operation

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

            Utilities.SystemClock = new TestingSystemClock();

            var instance = RunState(_state);

            TestingSystemClock.Current.Set(date);

            var task = await Task.WhenAny(tcs.Task, instance.Task);
            await task;

            tcs = new TaskCompletionSource<bool>();

            TestingSystemClock.Current.Set(date);

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
            await Workflow.StartedTask;

            var date = DateTime.Now.AddDays(3);
            _state.OnAsync(() => Workflow.WaitForDate(date)).GoTo(States.State2);

            Utilities.SystemClock = new TestingSystemClock();

            var instance = RunState(_state);

            await Workflow.ReadyTask;

            TestingSystemClock.Current.Set(date);

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TransitionToInnerStateShouldNotExecuteExitHandlersForParentState()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var counter = 0;
            _state.OnExit().Do(() => ++counter);

            var childState = CreateState(States.State1Child1).SubstateOf(_state);

            var instance = RunState(_state);

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(childState));

            await Workflow.ReadyTask;

            Assert.Same(childState, instance.Child.State);
            Assert.Equal(0, counter);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        // NOTE: This is needed in order to be able to execute actions from OnEnter() handlers, for actions that may have OnAction() handlers in parent states
        [Fact]
        public async Task TransitionToInnerStateShouldImportCurrentWorkflowOperation()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var childState = CreateState(States.State1Child1)
                .SubstateOf(_state)
                .OnEnter().Do(
                    () =>
                    {
                        Workflow.CreateOperation();
                        var operation = Workflow.TryStartOperation();
                        Assert.NotNull(operation);
                        operation.Dispose();
                    });

            var instance = RunState(_state);

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(childState));

            var task = await Task.WhenAny(Workflow.ReadyTask, instance.Task);
            await task;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TransitionFromInnerStateShouldNotExecuteExitHandlersForParentState()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var counter = 0;
            _state.OnExit().Do(() => ++counter);

            var childState = CreateState(States.State1Child1).SubstateOf(_state);

            var instance = RunState(childState);

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(_state));

            await Workflow.ReadyTask;

            Assert.Null(instance.Child);
            Assert.Equal(0, counter);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToInnerByOnEnterHandlers()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var childState = CreateState(States.State1Child1).SubstateOf(_state);
            _state.OnEnter().GoTo(States.State1Child1);

            var instance = RunState(_state);

            await Workflow.ReadyTask;

            Assert.Same(childState, instance.Child.State);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToItselfByOnEnterHandlers()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            _state.OnEnter().GoTo(States.State1);

            var childState = CreateState(States.State1Child1).SubstateOf(_state);
            var instance = RunState(childState);

            await Workflow.ReadyTask;

            Assert.Null(instance.Child);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToSiblingByOnEnterHandlers()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var counter = 0;
            _state.OnExit().Do(() => ++counter);
            var state2 = CreateState(States.State2);
            _state.OnEnter().GoTo(States.State2);

            var instance = RunState(_state);

            var transition = await instance.Task;

            Assert.Same(state2, transition.State);
            Assert.Equal(0, counter);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TargetStateCouldBeUpdatedToOtherByOnExitHandlers()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var childState = CreateState(States.State1Child1).SubstateOf(_state);
            _state.OnExit().GoTo(States.State1Child1);

            var counter = 0;
            _state.OnExit().Do(() => ++counter);

            var instance = RunState(_state);

            await Workflow.ReadyTask;

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            var transition = await instance.Task;
            Assert.Same(childState, transition.State);
            Assert.Equal(1, counter);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task OnActivateHandlersShouldBeCalledDuringStateRestoring()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var enterCounter = 0;
            _state.OnEnter().Do(() => ++enterCounter);

            var activateCounter = 0;
            _state.OnActivate().Do(() => ++activateCounter);

            var instance = RunState(_state, true);

            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            Assert.Equal(0, enterCounter);
            Assert.Equal(1, activateCounter);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task AllowedActionShouldBeReportedAsAllowed()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            var instance = RunState(_state);

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

            var instance = RunState(_state);

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

            var instance = RunState(_state);

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
            var instance = RunState(child);

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
            await Workflow.StartedTask;

            IDisposable operation = null;
            var tcs = new TaskCompletionSource<bool>();
            _state.OnAsync(
                async () =>
                {
                    if (operation != null)
                    {
                        await Workflow.WaitForDate(DateTime.MaxValue);
                        return 1;
                    }

                    return await Workflow.DoWorkflowTaskAsync(
                        async () =>
                        {
                            operation = await Workflow.WaitForReadyAndStartOperation();
                            tcs.SetResult(true);

                            return 1;
                        }).Unwrap();
                },
                getOperationForImport: () => operation)
                .Do(
                    async () =>
                    {
                        using (var operation2 = await Workflow.WaitForReadyAndStartOperation())
                        {
                            Assert.NotSame(operation, operation2);
                            await Task.Delay(1);
                            var t = operation.WaitForAllInnerOperationsCompletion();
                            Assert.NotEqual(TaskStatus.RanToCompletion, t.Status);
                        }
                    });

            var instance = RunState(_state);

            await tcs.Task;
            await Workflow.ReadyTask;
            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task OnAsync2ShouldImportOperationOnRequest()
        {
            StartWorkflow();
            await Workflow.StartedTask;

            IDisposable operation = null;
            var tcs = new TaskCompletionSource<bool>();
            _state.OnAsync(
                async () =>
                {
                    if (operation != null)
                    {
                        await Workflow.WaitForDate(DateTime.MaxValue);
                        return;
                    }

                    await Workflow.DoWorkflowTaskAsync(
                        async () =>
                        {
                            operation = await Workflow.WaitForReadyAndStartOperation();
                            tcs.SetResult(true);
                        }).Unwrap();
                },
                getOperationForImport: () => operation)
                .Do(
                    async () =>
                    {
                        using (var operation2 = await Workflow.WaitForReadyAndStartOperation())
                        {
                            Assert.NotSame(operation, operation2);
                            await Task.Delay(1);
                            var t = operation.WaitForAllInnerOperationsCompletion();
                            Assert.NotEqual(TaskStatus.RanToCompletion, t.Status);
                        }
                    });

            var instance = RunState(_state);

            await tcs.Task;
            await Workflow.ReadyTask;
            SetWorkflowTemporarily(Workflow, () => instance.InitiateTransitionTo(CreateState(States.State2)));

            await instance.Task;

            await CancelWorkflowAsync();
        }

        private State<States, string> CreateState(States state) => _baseStateTest.CreateState(state);

        private State<States, string>.StateInstance RunState(
            State<States, string> targetState,
            bool isRestoring = false)
        {
            Workflow.CreateOperation();

            var instance =
                _state.Run(new StateTransition<States, string>(targetState, Workflow.TryStartOperation(), isRestoring));
            var task = SetWorkflowTemporarily(Workflow, () => instance.Task); // This will actually execute state
            Assert.NotNull(task);
            return instance;
        }

        public class TestWorkflow : WorkflowBase
        {
            public void CreateOperation() => base.CreateOperation();

            public new IDisposable TryStartOperation() => base.TryStartOperation();

            public new void ResetOperation() => base.ResetOperation();

            protected override void OnActionsInit() => ConfigureAction("Action", synonym: "Action 1");

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }
    }
}
