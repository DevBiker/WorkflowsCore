using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkflowsCore.StateMachines
{
    public static class DotExtensions
    {
        private const string Indentation = "  ";

        public static string ToDotGraph<TWorkflow, TState, THiddenState>(IDependencyInjectionContainer diContainer)
            where TWorkflow : StateMachineWorkflow<TState, THiddenState>
        {
            var workflow = (StateMachineWorkflow<TState, THiddenState>)diContainer.Resolve(typeof(TWorkflow));
            workflow.OnStatesInit();
            return workflow.StateMachine.ToDotGraph();
        }

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
                yield return $"{Indentation}compound=true;";
            }

            var workaroundNodes = new WorkaroundNodes<TState, THiddenState>();
            var transitions = DefineStateTransitions(Indentation, states, workaroundNodes).ToList();
            var simpleRootStates = DeclareSimpleStates(
                Indentation,
                states.Where(s => s.Parent == null && !s.Children.Any()),
                workaroundNodes.GetWorkaroundNodes(null));
            foreach (var child in simpleRootStates)
            {
                yield return child;
            }

            foreach (var child in states.Where(s => s.Parent == null && s.Children.Any()))
            {
                foreach (var line in DeclareCompositeState(Indentation, child, workaroundNodes))
                {
                    yield return line;
                }
            }

            foreach (var transition in transitions)
            {
                yield return transition;
            }

            yield return "}";
        }

        private static IEnumerable<string> DeclareCompositeState<TState, THiddenState>(
            string indentation,
            State<TState, THiddenState> state,
            WorkaroundNodes<TState, THiddenState> workaroundNodes)
        {
            yield return $"{indentation}subgraph {GetDotId(state)} {{";

            var innerIndentation = $"{Indentation}{indentation}";
            var simpleStates = DeclareSimpleStates(
                innerIndentation,
                state.Children.Where(s => !s.Children.Any()),
                workaroundNodes.GetWorkaroundNodes(state));
            foreach (var child in simpleStates)
            {
                yield return child;
            }

            foreach (var child in state.Children.Where(s => s.Children.Any()))
            {
                foreach (var line in DeclareCompositeState(innerIndentation, child, workaroundNodes))
                {
                    yield return line;
                }
            }

            yield return $"{indentation}}}";
        }

        private static IEnumerable<string> DeclareSimpleStates<TState, THiddenState>(
            string indentation,
            IEnumerable<State<TState, THiddenState>> states,
            IEnumerable<string> workaroundNodes)
        {
            foreach (var line in DeclareSimpleStates(indentation, states))
            {
                yield return line;
            }

            foreach (var node in workaroundNodes)
            {
                yield return $"{indentation}{node} [style=invis];";
            }
        }

        private static IEnumerable<string> DeclareSimpleStates<TState, THiddenState>(
            string indentation,
            IEnumerable<State<TState, THiddenState>> states)
        {
            return
                from s in states.Where(s => !s.Children.Any())
                select $"{indentation}{s.GetDotId()}{GetProperties(description: s.Description)};";
        }

        private static string GetProperties(
            string description = null,
            string lTail = null,
            string lHead = null,
            string dir = null,
            bool? tailClip = null,
            bool? headClip = null)
        {
            var properties = GetPropertiesCore(description, lTail, lHead, dir, tailClip, headClip).ToList();
            return !properties.Any() ? string.Empty : $" [{string.Join(" ", properties)}]";
        }

        private static IEnumerable<string> GetPropertiesCore(
            string description,
            string lTail,
            string lHead,
            string dir,
            bool? tailClip,
            bool? headClip)
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

            if (dir != null)
            {
                yield return $"dir={dir}";
            }

            if (tailClip != null)
            {
                yield return $"tailclip={(tailClip.Value ? "true" : "false")}";
            }

            if (headClip != null)
            {
                yield return $"headclip={(headClip.Value ? "true" : "false")}";
            }
        }

        private static string GetDotId<TState, THiddenState>(this State<TState, THiddenState> state)
        {
            var dotId = !state.StateId.IsHiddenState ? state.StateId.Id.ToString() : state.StateId.HiddenId.ToString();
            return !state.Children.Any() ? dotId : $"cluster{dotId}";
        }

        private static IEnumerable<string> DefineStateTransitions<TState, THiddenState>(
            string indentation,
            IEnumerable<State<TState, THiddenState>> states,
            WorkaroundNodes<TState, THiddenState> workaroundNodes)
        {
            return
                from s in states
                from h in s.EnterHandlers.Concat(s.ActivationHandlers).Concat(s.OnAsyncHandlers).Concat(s.ExitHandlers)
                let targetStates = h.GetTargetStates(Enumerable.Empty<string>())
                where targetStates.Any()
                select DefineStateTransitions(indentation, s, targetStates, h.Description, workaroundNodes);
        }

        private static string DefineStateTransitions<TState, THiddenState>(
            string indentation,
            State<TState, THiddenState> srcState,
            IList<TargetState<TState, THiddenState>> targetStates,
            string description,
            WorkaroundNodes<TState, THiddenState> workaroundNodes)
        {
            var enumerate = targetStates.Count > 1;
            return string.Join(
                Environment.NewLine,
                targetStates.Select(
                    (t, i) => DefineStateTransition(
                        indentation,
                        srcState,
                        t,
                        description,
                        enumerate ? i + 1 : (int?)null,
                        workaroundNodes)));
        }

        private static State<TState, THiddenState> GetSimpleChild<TState, THiddenState>(
            State<TState, THiddenState> state)
        {
            var child = state.Children.First();
            return !child.Children.Any() ? child : GetSimpleChild(child);
        }

        private static string DefineStateTransition<TState, THiddenState>(
            string indentation,
            State<TState, THiddenState> srcState,
            TargetState<TState, THiddenState> targetState,
            string description,
            int? index,
            WorkaroundNodes<TState, THiddenState> workaroundNodes)
        {
            var isSrcStateSimple = !srcState.Children.Any();
            var srcDotId = (isSrcStateSimple ? srcState : GetSimpleChild(srcState)).GetDotId();
            var srcLTail = isSrcStateSimple ? null : srcState.GetDotId();
            var isTargetStateSimple = !targetState.State.Children.Any();
            var targetDotId = (isTargetStateSimple ? targetState.State : targetState.State.Children.First()).GetDotId();
            var targetLHead = isTargetStateSimple ? null : targetState.State.GetDotId();
            description = !targetState.Conditions.Any()
                ? description
                : string.Join(
                    " ",
                    new[] { description, $"[{string.Join(" AND ", targetState.Conditions)}]" }.Where(s => s != null));

            if (!string.IsNullOrEmpty(description) && index.HasValue)
            {
                description = $"{index}: {description}";
            }

            var compoundStateForSelfTransition = IsSelfTransitionFromCompoundState(srcState, targetState.State);
            if (compoundStateForSelfTransition == null)
            {
                var properties = GetProperties(description: description, lTail: srcLTail, lHead: targetLHead);
                return $"{indentation}{srcDotId} -> {targetDotId}{properties};";
            }

            var workaroundNode = workaroundNodes.AddWorkaroundNode(compoundStateForSelfTransition.Parent);
            var properties1 = GetProperties(lTail: srcLTail, dir: "none", headClip: false);
            var properties2 = GetProperties(description: description, lHead: targetLHead, tailClip: false);
            return string.Join(
                Environment.NewLine,
                $"{indentation}{srcDotId} -> {workaroundNode}{properties1};",
                $"{indentation}{workaroundNode} -> {targetDotId}{properties2};");
        }

        private static State<TState, THiddenState> IsSelfTransitionFromCompoundState<TState, THiddenState>(
            State<TState, THiddenState> srcState,
            State<TState, THiddenState> targetState)
        {
            if (srcState.Children.Any() && srcState == targetState)
            {
                return srcState;
            }

            var transition = new StateTransition<TState, THiddenState>(targetState);
            if (transition.FindPathFrom(srcState) != null)
            {
                return srcState;
            }

            transition = new StateTransition<TState, THiddenState>(srcState);
            if (transition.FindPathFrom(targetState) != null)
            {
                return targetState;
            }

            return null;
        }

        private class WorkaroundNodes<TState, THiddenState>
        {
            private readonly IList<string> _rootWorkaroundNodes = new List<string>();

            private readonly IDictionary<State<TState, THiddenState>, IList<string>> _workaroundNodesMap =
                new Dictionary<State<TState, THiddenState>, IList<string>>();

            private int _index = 1;

            public string AddWorkaroundNode(State<TState, THiddenState> parent)
            {
                var node = $"h{_index++}";
                if (parent == null)
                {
                    _rootWorkaroundNodes.Add(node);
                    return node;
                }

                IList<string> nodes;
                if (!_workaroundNodesMap.TryGetValue(parent, out nodes))
                {
                    nodes = new List<string>();
                    _workaroundNodesMap[parent] = nodes;
                }

                nodes.Add(node);
                return node;
            }

            public IEnumerable<string> GetWorkaroundNodes(State<TState, THiddenState> parent)
            {
                if (parent == null)
                {
                    return _rootWorkaroundNodes;
                }

                IList<string> nodes;
                return !_workaroundNodesMap.TryGetValue(parent, out nodes) ? Enumerable.Empty<string>() : nodes;
            }
        }
    }
}
