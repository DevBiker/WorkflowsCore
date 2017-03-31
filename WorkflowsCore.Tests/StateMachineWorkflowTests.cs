using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkflowsCore.StateMachines;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class StateMachineWorkflowTests : BaseWorkflowTest<StateMachineWorkflowTests.TestWorkflow>
    {
        public StateMachineWorkflowTests()
        {
            Workflow = new TestWorkflow();
        }

        public enum States
        {
            None,
            State1,
            State2
        }

        public enum HiddenStates
        {
            None,
            State2Hidden
        }

        [Fact]
        public async Task WorkflowShouldStartWithInitialState()
        {
            StartWorkflow();

            await Workflow.StartedTask;

            Assert.Equal(States.State1, Workflow.State);
            Assert.False(Workflow.IsLoaded);
            Assert.True(Workflow.WasIn(States.State1));

            await Workflow.ExecuteActionAsync(TestWorkflow.Action1);
            await Workflow.ReadyTask;

            Assert.Equal(States.State2, Workflow.State);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WorkflowShouldProperlyRestoreState()
        {
            var loadedData = new Dictionary<string, object>
            {
                ["StatesHistory"] = new List<States> { States.State1, States.State2 }
            };
            StartWorkflow(loadedWorkflowData: loadedData);

            await Workflow.StartedTask;

            Assert.Equal(States.State2, Workflow.State);
            Assert.True(Workflow.IsLoaded);
            Assert.False(Workflow.WasIn(States.State2));

            await Workflow.ExecuteActionAsync(TestWorkflow.Action1);
            await Workflow.ReadyTask;

            Assert.Equal(States.State1, Workflow.State);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WorkflowShouldSupportTransitionsToInnerHiddenStates()
        {
            StartWorkflow();

            await Workflow.ExecuteActionAsync(TestWorkflow.Action2);
            await Workflow.ReadyTask;

            Assert.Equal(States.State2, Workflow.State);
            Assert.Equal(HiddenStates.State2Hidden, Workflow.HiddenState.Single());

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task WorkflowShouldProperlyRestoreHiddenState()
        {
            var loadedData = new Dictionary<string, object>
            {
                ["StatesHistory"] = new List<States> { States.State1, States.State2 },
                ["HiddenState"] = new List<HiddenStates> { HiddenStates.State2Hidden }
            };
            StartWorkflow(loadedWorkflowData: loadedData);

            await Workflow.StartedTask;

            Assert.Equal(States.State2, Workflow.State);
            Assert.Equal(HiddenStates.State2Hidden, Workflow.HiddenState.Single());
            Assert.True(Workflow.IsLoaded);
            Assert.False(Workflow.WasIn(States.State2));

            await Workflow.ExecuteActionAsync(TestWorkflow.Action2);
            await Workflow.ReadyTask;

            Assert.Equal(States.State1, Workflow.State);
            Assert.False(Workflow.HiddenState.Any());

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task CompleteWorkflowShouldCompleteIt()
        {
            StartWorkflow();

            await Workflow.StartedTask;

            Assert.Equal(States.State1, Workflow.State);

            Workflow.CompleteWorkflow();

            await WaitUntilWorkflowCompleted();
        }

        public class TestWorkflow : StateMachineWorkflow<States, HiddenStates>
        {
            public const string Action1 = nameof(Action1);
            public const string Action2 = nameof(Action2);

            public new States State => base.State;

            public new IList<HiddenStates> HiddenState => base.HiddenState;

            public new bool IsLoaded => base.IsLoaded;

            public new void CompleteWorkflow() => base.CompleteWorkflow();

            public new bool WasIn(States state) => base.WasIn(state);

            protected override void OnActionsInit()
            {
                ConfigureAction(Action1);
                ConfigureAction(Action2);
            }

            protected override void OnStatesInit()
            {
                ConfigureState(States.State1)
                    .OnEnter().Do(() => Task.Delay(1))
                    .OnAction(Action1).GoTo(States.State2)
                    .OnAction(Action2).GoTo(HiddenStates.State2Hidden);

                ConfigureState(States.State2)
                    .OnEnter().Do(() => Task.Delay(1))
                    .OnAction(Action1).GoTo(States.State1);

                ConfigureHiddenState(HiddenStates.State2Hidden)
                    .OnEnter().Do(() => Task.Delay(1))
                    .SubstateOf(States.State2)
                    .OnAction(Action2).GoTo(States.State1);

                SetInitialState(States.State1);
            }
        }
    }
}
