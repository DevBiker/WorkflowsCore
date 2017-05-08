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
            HiddenState2,
            HiddenState3
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
            sm.ConfigureState(States.State2);

            sm.ConfigureState(States.State1)
                .HasDescription("State 1");

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
        public void HiddenTransitionsShouldBeIgnored()
        {
            var sm = new StateMachine<States, HiddenStates>();
            sm.ConfigureState(States.State1)
                .OnAsync(() => Task.CompletedTask, "On Event 1", isHidden: true).GoTo(States.State2);
            sm.ConfigureState(States.State2)
                .OnAsync(() => Task.CompletedTask).GoTo(States.State2);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  State1;",
                "  State2;",
                "  State2 -> State2;",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void HiddenStatesShouldBeSkipped()
        {
            var sm = new StateMachine<States, HiddenStates>();
            sm.ConfigureState(States.State2)
                .OnAsync(() => Task.CompletedTask).GoTo(States.State2);

            sm.ConfigureHiddenState(HiddenStates.HiddenState2)
                .SubstateOf(States.State2)
                .Hide();

            sm.ConfigureState(States.State1)
                .Hide();

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .Hide();

            sm.ConfigureState(States.State3)
                .SubstateOf(HiddenStates.HiddenState1);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  State2;",
                "  State2 -> State2;",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void HiddenStatesOfCompoundStatesShouldBeSkipped()
        {
            var sm = new StateMachine<States, HiddenStates>();
            sm.ConfigureState(States.State2);
            sm.ConfigureHiddenState(HiddenStates.HiddenState2)
                .SubstateOf(States.State2);

            sm.ConfigureState(States.State1)
                .SubstateOf(HiddenStates.HiddenState2)
                .Hide();

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .Hide();

            sm.ConfigureState(States.State3)
                .SubstateOf(HiddenStates.HiddenState1);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound=true;",
                "  subgraph clusterState2 {",
                "    HiddenState2;",
                "  }",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CompoundStatesShouldBeProperlyDeclared()
        {
            var sm = new StateMachine<States, HiddenStates>();

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
                "  compound=true;",
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

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound=true;",
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

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .SubstateOf(States.State1);

            sm.ConfigureState(States.State2)
                .OnAsync(() => Task.CompletedTask).GoTo(States.State1);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound=true;",
                "  State2;",
                "  subgraph clusterState1 {",
                "    HiddenState1;",
                "  }",
                "  State2 -> HiddenState1 [lhead=clusterState1];",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SelfTransitionFromCompoundStateShouldBeSupported()
        {
            var sm = new StateMachine<States, HiddenStates>();

            sm.ConfigureState(States.State1)
                .OnAsync(() => Task.CompletedTask).GoTo(HiddenStates.HiddenState1);

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .SubstateOf(States.State1);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound=true;",
                "  h1 [style=invis];",
                "  subgraph clusterState1 {",
                "    HiddenState1;",
                "  }",
                "  HiddenState1 -> h1 [ltail=clusterState1 dir=none headclip=false];",
                "  h1 -> HiddenState1 [tailclip=false];",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SelfTransitionFromCompoundStateShouldBeSupported2()
        {
            var sm = new StateMachine<States, HiddenStates>();

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .SubstateOf(States.State1)
                .OnAsync(() => Task.CompletedTask).GoTo(States.State1);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound=true;",
                "  h1 [style=invis];",
                "  subgraph clusterState1 {",
                "    HiddenState1;",
                "  }",
                "  HiddenState1 -> h1 [dir=none headclip=false];",
                "  h1 -> HiddenState1 [lhead=clusterState1 tailclip=false];",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SimpleConditionalTransitionsShouldBeSupported()
        {
            var sm = new StateMachine<States, HiddenStates>();
            sm.ConfigureState(States.State1)
                .OnAsync(() => Task.CompletedTask, "On Event 1").If(() => true, "On Condition 1").GoTo(States.State2);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  State1;",
                "  State2;",
                "  State1 -> State2 [label=\"On Event 1 [On Condition 1]\"];",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ComplexConditionalTransitionsShouldBeSupported()
        {
            var sm = new StateMachine<States, HiddenStates>();
            sm.ConfigureState(States.State1)
                .OnAsync(() => Task.CompletedTask, "On Event 1")
                .If(() => true, "On Condition 1")
                .If(() => true, "On Condition 2")
                .GoTo(States.State2);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  State1;",
                "  State2;",
                "  State1 -> State2 [label=\"On Event 1 [On Condition 1 AND On Condition 2]\"];",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void IfThereAreMultipleTargetStatesFromSingleTransitionTheyShouldBeEnumerated()
        {
            var sm = new StateMachine<States, HiddenStates>();
            sm.ConfigureState(States.State1)
                .OnAsync(() => Task.CompletedTask, "On Event 1")
                .IfThenGoTo(() => true, States.State3, "On Condition 1")
                .GoTo(States.State2);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  State1;",
                "  State2;",
                "  State3;",
                "  State1 -> State3 [label=\"1: On Event 1 [On Condition 1]\"];",
                "  State1 -> State2 [label=\"2: On Event 1\"];",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TransitionFromInnerHiddenStateShouldBeSupported()
        {
            var sm = new StateMachine<States, HiddenStates>();

            sm.ConfigureState(States.State1);

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .SubstateOf(States.State1)
                .Hide()
                .OnAsync(() => Task.CompletedTask).GoTo(States.State2);

            sm.ConfigureState(States.State3)
                .SubstateOf(States.State1);

            sm.ConfigureHiddenState(HiddenStates.HiddenState2)
                .Hide();

            sm.ConfigureHiddenState(HiddenStates.HiddenState3)
                .SubstateOf(HiddenStates.HiddenState2)
                .OnAsync(() => Task.CompletedTask).GoTo(States.State2);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound=true;",
                "  State2;",
                "  subgraph clusterState1 {",
                "    State3;",
                "  }",
                "  State3 -> State2 [ltail=clusterState1];",
                "}");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TransitionToInnerHiddenStateShouldBeSupported()
        {
            var sm = new StateMachine<States, HiddenStates>();

            sm.ConfigureState(States.State1);

            sm.ConfigureHiddenState(HiddenStates.HiddenState1)
                .SubstateOf(States.State1)
                .Hide();

            sm.ConfigureState(States.State3)
                .SubstateOf(States.State1);

            sm.ConfigureHiddenState(HiddenStates.HiddenState2)
                .Hide();

            sm.ConfigureHiddenState(HiddenStates.HiddenState3)
                .SubstateOf(HiddenStates.HiddenState2);

            sm.ConfigureState(States.State2)
                .OnAsync(() => Task.CompletedTask).GoTo(HiddenStates.HiddenState1)
                .OnAsync(() => Task.CompletedTask).GoTo(HiddenStates.HiddenState3);

            var actual = sm.ToDotGraph();

            var expected = string.Join(
                Environment.NewLine,
                "digraph {",
                "  compound=true;",
                "  State2;",
                "  subgraph clusterState1 {",
                "    State3;",
                "  }",
                "  State2 -> State3 [lhead=clusterState1];",
                "}");
            Assert.Equal(expected, actual);
        }
    }
}
