using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace WorkflowsCore.StateMachines
{
    public class StateTransition<TState, TInternalState>
    {
        private readonly IDisposable _workflowOperation;

        public StateTransition(
            State<TState, TInternalState> state,
            IDisposable workflowOperation,
            bool isRestoringState = false,
            Action<StateTransition<TState, TInternalState>> onStateChangedHandler = null)
            : this(state)
        {
            if (workflowOperation == null)
            {
                throw new ArgumentNullException(nameof(workflowOperation));
            }

            _workflowOperation = workflowOperation;
            IsRestoringState = isRestoringState;
            OnStateChangedHandler = onStateChangedHandler;
        }

        // NOTE: We do not copy IsRestoringState - transition that interrupted restoring state is a transition to new state, but not restoring
        public StateTransition(State<TState, TInternalState> state, StateTransition<TState, TInternalState> transition)
            : this(state, transition._workflowOperation, onStateChangedHandler: transition.OnStateChangedHandler)
        {
        }

        internal StateTransition(State<TState, TInternalState> state)
        {
            State = state;
            Path = GetPath();
        }

        public State<TState, TInternalState> State { get; }

        public State<TState, TInternalState>.StateInstance StateInstance { get; private set; }

        public IReadOnlyCollection<State<TState, TInternalState>> Path { get; }

        public bool IsRestoringState { get; }

        public Action<StateTransition<TState, TInternalState>> OnStateChangedHandler { get; }

        internal IDisposable WorkflowOperation => _workflowOperation;

        public void CompleteTransition(State<TState, TInternalState>.StateInstance stateInstance)
        {
            StateInstance = stateInstance;
            OnStateChangedHandler?.Invoke(this);
            _workflowOperation.Dispose();
        }

        public IList<State<TState, TInternalState>> FindPathFrom(State<TState, TInternalState> parentState)
        {
            var res = Path.SkipWhile(s => s != parentState).Skip(1).ToList();
            return res.Any() ? res : null;
        }

        public State<TState, TInternalState>.StateInstance PerformTransition() => Path.First().Run(this);

        private IReadOnlyCollection<State<TState, TInternalState>> GetPath()
        {
            var path = new List<State<TState, TInternalState>>();
            var cur = State;
            do
            {
                path.Add(cur);
                cur = cur.Parent;
            }
            while (cur != null);

            path.Reverse();
            return new ReadOnlyCollection<State<TState, TInternalState>>(path);
        }
    }
}
