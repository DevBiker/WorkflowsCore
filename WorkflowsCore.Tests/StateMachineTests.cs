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

        private enum States
        {
            State1,
            State1Child1,
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
        public void ConfigureInternalStateShouldCreateInternalStateIfItDoesNotExist()
        {
            var state = _stateMachine.ConfigureInternalState("Internal State 1");
            var state2 = _stateMachine.ConfigureInternalState("Internal State 1");

            Assert.NotNull(state);
            Assert.Same(state, state2);
        }

        [Fact]
        public async Task RunShouldStartInitialStateAndImportWorkflowOperation()
        {
            _stateMachine.ConfigureState(States.State1)
                .OnEnter().Do(
                    () =>
                    {
                        Workflow.CreateOperation();
                        var operation = Workflow.TryStartOperation();
                        Assert.NotNull(operation);
                        operation.Dispose();
                    });
            var state = _stateMachine.ConfigureState(States.State1Child1).SubstateOf(States.State1);

            Workflow = new TestWorkflow();
            StartWorkflow();

            var tcsCurState = new TaskCompletionSource<State<States, string>>();
            var instance = await Workflow.DoWorkflowTaskAsync(
                w =>
                {
                    var i = _stateMachine.Run(w, States.State1Child1, false, t => tcsCurState.SetResult(t.State));
                    Assert.NotNull(i.Task); // This actually will run state machine because its lazy
                    return i;
                });

            var task = await Task.WhenAny(tcsCurState.Task, instance.Task);
            await task;

            Assert.Same(state, tcsCurState.Task.Result);

            await CancelWorkflowAsync();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => instance.Task);

            Assert.IsType<TaskCanceledException>(ex);
        }

        // NOTE: Import of current workflow operation is needed in order to be able to execute actions from OnEnter() handlers, for actions that may have OnAction() handlers in parent states
        [Fact]
        public async Task RunShouldHandleSubsequentTransitionsAndImportCurrentWorkflowOperation()
        {
            var date = DateTime.Now.AddDays(3);
            var state2 = _stateMachine.ConfigureState(States.State2)
                .OnEnter().Do(
                    () =>
                    {
                        Workflow.CreateOperation();
                        var operation = Workflow.TryStartOperation();
                        Assert.NotNull(operation);
                        operation.Dispose();
                    });
            _stateMachine.ConfigureState(States.State1)
                .OnAsync(() => Workflow.WaitForDate(date)).GoTo(States.State2);

            Utilities.SystemClock = new TestingSystemClock();
            Workflow = new TestWorkflow();
            StartWorkflow();

            var tcsCurState = new TaskCompletionSource<State<States, string>>();
            var counter = 0;
            var instance = await Workflow.DoWorkflowTaskAsync(
                w =>
                {
                    var i = _stateMachine.Run(
                        w,
                        States.State1,
                        false,
                        t =>
                        {
                            if (counter++ > 0)
                            {
                                tcsCurState.SetResult(t.State);
                            }
                        });
                    Assert.NotNull(i.Task); // This actually will run state machine because its lazy
                    return i;
                });

            await Workflow.ReadyTask;

            TestingSystemClock.Current.SetCurrentTime(date);

            var task = await Task.WhenAny(tcsCurState.Task, instance.Task);
            await task;

            Assert.Same(state2, tcsCurState.Task.Result);

            await CancelWorkflowAsync();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => instance.Task);

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task RunShouldShouldThrowAoreIfInitialStateWasNotConfigured()
        {
            Workflow = new TestWorkflow();
            StartWorkflow();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Workflow.DoWorkflowTaskAsync(w => _stateMachine.Run(w, States.State1, true)));

            Assert.IsType<ArgumentOutOfRangeException>(ex);

            await CancelWorkflowAsync();
        }

        [Fact]
        public void RunShouldShouldThrowIoeIfIsInvokedOutsideOfWorkflowTaskScheduler()
        {
            // ReSharper disable once PossibleNullReferenceException
            var ex = Record.Exception(() => _stateMachine.Run(new TestWorkflow(), States.State1, true));

            Assert.IsType<InvalidOperationException>(ex);
        }

        public class TestWorkflow : WorkflowBase
        {
            public void CreateOperation() => base.CreateOperation();

            public new IDisposable TryStartOperation() => base.TryStartOperation();

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }
    }
}
