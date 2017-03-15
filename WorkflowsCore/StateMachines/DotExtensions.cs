using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkflowsCore.StateMachines
{
    public static class DotExtensions
    {
        private const string Indentation = "  ";

        public static string ToDotGraph<TState, THiddenState>(
            this StateMachineWorkflow<TState, THiddenState> stateMachineWorkflow)
        {
            return stateMachineWorkflow.StateMachine.ToDotGraph();
        }

        public static string ToDotGraph<TState, THiddenState>(this StateMachine<TState, THiddenState> stateMachine) => 
            string.Join(Environment.NewLine, ToDotGraphCore(stateMachine));

        private static IEnumerable<string> ToDotGraphCore<TState, THiddenState>(
            StateMachine<TState, THiddenState> stateMachine)
        {
            yield return "digraph {";

            var states = stateMachine.States.Concat(stateMachine.HiddenStates).OrderBy(GetDotId).ToList();
            if (states.Any(s => s.Children.Any()))
            {
                yield return $"{Indentation}compound = true;";
            }

            var simpleRootStates = states.Where(s => s.Parent == null && !s.Children.Any());
            foreach (var child in DeclareSimpleStates(Indentation, simpleRootStates))
            {
                yield return child;
            }

            foreach (var child in states.Where(s => s.Parent == null && s.Children.Any()))
            {
                foreach (var line in DeclareCompositeState(Indentation, child))
                {
                    yield return line;
                }
            }

            foreach (var child in DefineStateTransitions(Indentation, states))
            {
                yield return child;
            }

            yield return "}";
        }

        private static IEnumerable<string> DeclareCompositeState<TState, THiddenState>(
            string indentation,
            State<TState, THiddenState> state)
        {
            yield return $"{indentation}subgraph {GetDotId(state)} {{";

            var innerIndentation = $"{Indentation}{indentation}";
            foreach (var child in DeclareSimpleStates(innerIndentation, state.Children.Where(s => !s.Children.Any())))
            {
                yield return child;
            }

            foreach (var child in state.Children.Where(s => s.Children.Any()))
            {
                foreach (var line in DeclareCompositeState(innerIndentation, child))
                {
                    yield return line;
                }
            }

            yield return $"{indentation}}}";
        }

        private static IEnumerable<string> DeclareSimpleStates<TState, THiddenState>(
            string indentation,
            IEnumerable<State<TState, THiddenState>> states)
        {
            return
                from s in states.Where(s => !s.Children.Any())
                select $"{indentation}{s.GetDotId()}{GetProperties(description: s.Description)};";
        }

        private static string GetProperties(string description = null, string lTail = null, string lHead = null)
        {
            var properties = GetPropertiesCore(description, lTail, lHead).ToList();
            return !properties.Any() ? string.Empty : $" [{string.Join(" ", properties)}]";
        }

        private static IEnumerable<string> GetPropertiesCore(string description, string lTail, string lHead)
        {
            if (description != null)
            {
                yield return $"label=\"{description}\"";
            }

            if (lTail != null)
            {
                yield return $"ltail={lTail}";
            }

            if (lHead != null)
            {
                yield return $"lhead={lHead}";
            }
        }

        private static string GetDotId<TState, THiddenState>(this State<TState, THiddenState> state)
        {
            var dotId = !state.StateId.IsHiddenState ? state.StateId.Id.ToString() : state.StateId.HiddenId.ToString();
            return !state.Children.Any() ? dotId : $"cluster{dotId}";
        }

        private static IEnumerable<string> DefineStateTransitions<TState, THiddenState>(
            string indentation,
            IEnumerable<State<TState, THiddenState>> states)
        {
            return
                from s in states
                from h in s.EnterHandlers.Concat(s.ActivationHandlers).Concat(s.OnAsyncHandlers).Concat(s.ExitHandlers)
                from t in h.GetTargetStates(Enumerable.Empty<string>())
                let isSrcStateSimple = !s.Children.Any()
                let srcDotId = (isSrcStateSimple ? s : GetSimpleChild(s)).GetDotId()
                let srcLTail = isSrcStateSimple ? null : s.GetDotId()
                let isTargetStateSimple = !t.State.Children.Any()
                let targetDotId = (isTargetStateSimple ? t.State : t.State.Children.First()).GetDotId()
                let targetLHead = isTargetStateSimple ? null : t.State.GetDotId()
                let properties = GetProperties(description: h.Description, lTail: srcLTail, lHead: targetLHead)
                select $"{indentation}{srcDotId} -> {targetDotId}{properties};";
        }

        private static State<TState, THiddenState> GetSimpleChild<TState, THiddenState>(
            State<TState, THiddenState> state)
        {
            var child = state.Children.First();
            return !child.Children.Any() ? child : GetSimpleChild(child);
        }
    }
}
