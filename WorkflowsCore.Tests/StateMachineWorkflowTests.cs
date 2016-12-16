using System.Collections.Generic;
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
            State1,
            State2
        }

        [Fact]
        public async Task WorkflowShouldStartWithInitialState()
        {
            StartWorkflow();

            await Workflow.StartedTask;

            await WaitForReady();

            Assert.Equal(States.State1, Workflow.State);

            await Workflow.ExecuteActionAsync(TestWorkflow.Action1);
            await WaitForReady();

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

            await WaitForReady();

            Assert.Equal(States.State2, Workflow.State);

            await Workflow.ExecuteActionAsync(TestWorkflow.Action1);
            await WaitForReady();

            Assert.Equal(States.State1, Workflow.State);

            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task CompleteWorkflowShouldCompleteIt()
        {
            StartWorkflow();

            await Workflow.StartedTask;

            await WaitForReady();

            Assert.Equal(States.State1, Workflow.State);

            Workflow.CompleteWorkflow();

            await WaitUntilWorkflowCompleted();
        }

        private async Task WaitForReady()
        { // TODO:
            await Workflow.DoWorkflowTaskAsync(async () => await Task.Delay(1)).Unwrap();
            await Workflow.ReadyTask;
        }

        public class TestWorkflow : StateMachineWorkflow<States>
        {
            public const string Action1 = nameof(Action1);

            public new States State => base.State;

            public new void CompleteWorkflow() => base.CompleteWorkflow();

            protected override void OnActionsInit()
            {
                ConfigureAction(Action1);
            }

            protected override void OnStatesInit()
            {
                ConfigureState(States.State1)
                    .OnAction(Action1).GoTo(States.State2);

                ConfigureState(States.State2)
                    .OnAction(Action1).GoTo(States.State1);

                SetInitialState(States.State1);
            }
        }
    }
}
