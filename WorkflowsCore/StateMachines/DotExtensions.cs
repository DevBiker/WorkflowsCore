using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkflowsCore.StateMachines
{
    public static class DotExtensions
    {
        private const string Indentation = "  ";

        public static string ToDotGraph<TWorkflow, TState, TInternalState>(IDependencyInjectionContainer diContainer)
            where TWorkflow : StateMachineWorkflow<TState, TInternalState>
        {
            var workflow = (StateMachineWorkflow<TState, TInternalState>)diContainer.Resolve(typeof(TWorkflow));
            workflow.OnStatesInit();
            return workflow.StateMachine.ToDotGraph();
        }

        public static string ToDotGraph<TState, TInternalState>(
            this StateMachineWorkflow<TState, TInternalState> stateMachineWorkflow)
        {
            return stateMachineWorkflow.StateMachine.ToDotGraph();
        }

        public static string ToDotGraph<TState, TInternalState>(this StateMachine<TState, TInternalState> stateMachine) =>
            string.Join(Environment.NewLine, ToDotGraphCore(stateMachine));

        private static IEnumerable<string> ToDotGraphCore<TState, TInternalState>(
            StateMachine<TState, TInternalState> stateMachine)
        {
            yield return "digraph {";

            var states = stateMachine.States.Concat(stateMachine.InternalStates).OrderBy(GetDotId).ToList();
            if (states.Any(s => !s.IsHidden && !IsSimpleState(s)))
            {
                yield return $"{Indentation}compound=true;";
            }

            var workaroundNodes = new WorkaroundNodes<TState, TInternalState>();
            var transitions = DefineStateTransitions(Indentation, states, workaroundNodes).ToList();
            var simpleRootStates = DeclareSimpleStates(
                Indentation,
                states.Where(s => s.Parent == null && !s.IsHidden && IsSimpleState(s)),
                workaroundNodes.GetWorkaroundNodes(null));
            foreach (var child in simpleRootStates)
            {
                yield return child;
            }

            foreach (var child in states.Where(s => s.Parent == null && !s.IsHidden && !IsSimpleState(s)))
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

        private static IEnumerable<string> DeclareCompositeState<TState, TInternalState>(
            string indentation,
            State<TState, TInternalState> state,
            WorkaroundNodes<TState, TInternalState> workaroundNodes)
        {
            yield return $"{indentation}subgraph {GetDotId(state)} {{";

            var innerIndentation = $"{Indentation}{indentation}";
            var simpleStates = DeclareSimpleStates(
                innerIndentation,
                state.Children.Where(s => !s.IsHidden && IsSimpleState(s)),
                workaroundNodes.GetWorkaroundNodes(state));
            foreach (var child in simpleStates)
            {
                yield return child;
            }

            foreach (var child in state.Children.Where(s => !s.IsHidden && !IsSimpleState(s)))
            {
                foreach (var line in DeclareCompositeState(innerIndentation, child, workaroundNodes))
                {
                    yield return line;
                }
            }

            yield return $"{indentation}}}";
        }

        private static IEnumerable<string> DeclareSimpleStates<TState, TInternalState>(
            string indentation,
            IEnumerable<State<TState, TInternalState>> states,
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

        private static IEnumerable<string> DeclareSimpleStates<TState, TInternalState>(
            string indentation,
            IEnumerable<State<TState, TInternalState>> states)
        {
            return
                from s in states.Where(IsSimpleState)
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

        private static string GetDotId<TState, TInternalState>(this State<TState, TInternalState> state)
        {
            var dotId = !state.StateId.IsInternalState ? state.StateId.Id.ToString() : state.StateId.InternalState.ToString();
            return IsSimpleState(state) ? dotId : $"cluster{dotId}";
        }

        private static bool IsSimpleState<TState, TInternalState>(State<TState, TInternalState> state) => 
            state.Children.All(c => c.IsHidden);

        private static IEnumerable<string> DefineStateTransitions<TState, TInternalState>(
            string indentation,
            IEnumerable<State<TState, TInternalState>> states,
            WorkaroundNodes<TState, TInternalState> workaroundNodes)
        {
            return
                from s in states
                from h in s.EnterHandlers.Concat(s.ActivationHandlers)
                    .Concat(s.OnAsyncHandlers)
                    .Concat(s.ExitHandlers)
                    .Where(h => !h.IsHidden)
                let v = TryGetVisibleSelfOrParent(s)
                where v != null
                let targetStates = h.GetTargetStates(Enumerable.Empty<string>())
                    .Select(
                        t => new TargetState<TState, TInternalState>(t.Conditions, TryGetVisibleSelfOrParent(t.State)))
                    .Where(t => t.State != null)
                    .ToList()
                where targetStates.Any()
                select DefineStateTransitions(indentation, v, targetStates, h.Description, workaroundNodes);
        }

        private static State<TState, TInternalState> TryGetVisibleSelfOrParent<TState, TInternalState>(
            State<TState, TInternalState> state)
        {
            if (!state.IsHidden)
            {
                return state;
            }

            for (var parent = state.Parent; parent != null; parent = parent.Parent)
            {
                if (!parent.IsHidden)
                {
                    return parent;
                }
            }

            return null;
        }

        private static string DefineStateTransitions<TState, TInternalState>(
            string indentation,
            State<TState, TInternalState> srcState,
            IList<TargetState<TState, TInternalState>> targetStates,
            string description,
            WorkaroundNodes<TState, TInternalState> workaroundNodes)
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

        private static State<TState, TInternalState> GetSimpleChild<TState, TInternalState>(
            State<TState, TInternalState> state)
        {
            var child = state.Children.First(c => !c.IsHidden);
            return IsSimpleState(child) ? child : GetSimpleChild(child);
        }

        private static string DefineStateTransition<TState, TInternalState>(
            string indentation,
            State<TState, TInternalState> srcState,
            TargetState<TState, TInternalState> targetState,
            string description,
            int? index,
            WorkaroundNodes<TState, TInternalState> workaroundNodes)
        {
            var isSrcStateSimple = IsSimpleState(srcState);
            var srcDotId = (isSrcStateSimple ? srcState : GetSimpleChild(srcState)).GetDotId();
            var srcLTail = isSrcStateSimple ? null : srcState.GetDotId();
            var isTargetStateSimple = IsSimpleState(targetState.State);
            var targetDotId = (isTargetStateSimple ? targetState.State : GetSimpleChild(targetState.State)).GetDotId();
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

        private static State<TState, TInternalState> IsSelfTransitionFromCompoundState<TState, TInternalState>(
            State<TState, TInternalState> srcState,
            State<TState, TInternalState> targetState)
        {
            if (!IsSimpleState(srcState) && srcState == targetState)
            {
                return srcState;
            }

            var transition = new StateTransition<TState, TInternalState>(targetState);
            if (transition.FindPathFrom(srcState) != null)
            {
                return srcState;
            }

            transition = new StateTransition<TState, TInternalState>(srcState);
            if (transition.FindPathFrom(targetState) != null)
            {
                return targetState;
            }

            return null;
        }

        private class WorkaroundNodes<TState, TInternalState>
        {
            private readonly IList<string> _rootWorkaroundNodes = new List<string>();

            private readonly IDictionary<State<TState, TInternalState>, IList<string>> _workaroundNodesMap =
                new Dictionary<State<TState, TInternalState>, IList<string>>();

            private int _index = 1;

            public string AddWorkaroundNode(State<TState, TInternalState> parent)
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

            public IEnumerable<string> GetWorkaroundNodes(State<TState, TInternalState> parent)
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
