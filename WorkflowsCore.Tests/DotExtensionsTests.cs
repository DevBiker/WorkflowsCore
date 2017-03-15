using System;
using System.Threading.Tasks;
using WorkflowsCore.StateMachines;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class DotExtensionsTests
    {
        private enum States
        {
            State1,
            State2,
            State3
        }

        private enum HiddenStates
        {
            HiddenState1,
            HiddenState2
        }

        [Fact]
        public void ForEmptyStateMachineEmptyGraphShouldBeReturned()
        {
            var sm = new StateMachine<States, HiddenStates>();

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine, 
                "digraph {", 
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void IfSimpleStateHasNonNullDescriptionItShouldBeUsedAsLabelForIt()
        {
            var sm = new StateMachine<States, HiddenStates>();
            sm.ConfigureState(States.State1)
                .HasDescription("State 1");

            sm.ConfigureState(States.State2);

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .HasDescription(string.Empty);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  HiddenState1 [label=\"\"];",
                "  State1 [label=\"State 1\"];",
                "  State2;",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TransitionsBetweenSimpleStatesShouldBeSupported()
        {
            var sm = new StateMachine<States, HiddenStates>();
            sm.ConfigureState(States.State1)
                .OnAsync(() => Task.CompletedTask, "On Event 1").GoTo(States.State2);
            sm.ConfigureState(States.State2)
                .OnAsync(() => Task.CompletedTask).GoTo(States.State2);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  State1;",
                "  State2;",
                "  State1 -> State2 [label=\"On Event 1\"];",
                "  State2 -> State2;",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CompoundStatesShouldBeProperlyDeclared()
        {
            var sm = new StateMachine<States, HiddenStates>();

            sm.ConfigureState(States.State1);

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .SubstateOf(States.State1);

            sm.ConfigureState(States.State2)
                .SubstateOf(States.State1);

            sm.ConfigureState(States.State3)
                .SubstateOf(States.State2);

            sm.ConfigureHiddenState(HiddenStates.HiddenState2);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound = true;",
                "  HiddenState2;",
                "  subgraph clusterState1 {",
                "    HiddenState1;",
                "    subgraph clusterState2 {",
                "      State3;",
                "    }",
                "  }",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TransitionFromCompoundStateToSimpleStateShouldBeSupported()
        {
            var sm = new StateMachine<States, HiddenStates>();

            sm.ConfigureState(States.State1)
                .OnAsync(() => Task.CompletedTask).GoTo(States.State2);

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .SubstateOf(States.State1);

            sm.ConfigureState(States.State2);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound = true;",
                "  State2;",
                "  subgraph clusterState1 {",
                "    HiddenState1;",
                "  }",
                "  HiddenState1 -> State2 [ltail=clusterState1];",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TransitionFromSimpleStateToCompoundStateShouldBeSupported()
        {
            var sm = new StateMachine<States, HiddenStates>();

            sm.ConfigureState(States.State1);

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .SubstateOf(States.State1);

            sm.ConfigureState(States.State2)
                .OnAsync(() => Task.CompletedTask).GoTo(States.State1);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound = true;",
                "  State2;",
                "  subgraph clusterState1 {",
                "    HiddenState1;",
                "  }",
                "  State2 -> HiddenState1 [lhead=clusterState1];",
                "}");
            Assert.Equal(expected, actual);
        }
    }
}
