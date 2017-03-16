using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace WorkflowsCore.StateMachines
{
    public class StateTransition<TState, THiddenState>
    {
        private readonly IDisposable _workflowOperation;

        public StateTransition(
            State<TState, THiddenState> state,
            IDisposable workflowOperation,
            bool isRestoringState = false,
            Action<StateTransition<TState, THiddenState>> onStateChangedHandler = null) 
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
        public StateTransition(State<TState, THiddenState> state, StateTransition<TState, THiddenState> transition)
            : this(state, transition._workflowOperation, onStateChangedHandler: transition.OnStateChangedHandler)
        {
        }

        internal StateTransition(State<TState, THiddenState> state)
        {
            State = state;
            Path = GetPath();
        }

        public State<TState, THiddenState> State { get; }

        public IReadOnlyCollection<State<TState, THiddenState>> Path { get; }

        public bool IsRestoringState { get; }

        public Action<StateTransition<TState, THiddenState>> OnStateChangedHandler { get; }

        public void CompleteTransition()
        {
            OnStateChangedHandler?.Invoke(this);
            _workflowOperation.Dispose();
        }

        public IList<State<TState, THiddenState>> FindPathFrom(State<TState, THiddenState> parentState)
        {
            var res = Path.SkipWhile(s => s != parentState).Skip(1).ToList();
            return res.Any() ? res : null;
        }

        public State<TState, THiddenState>.StateInstance PerformTransition() => Path.First().Run(this);

        private IReadOnlyCollection<State<TState, THiddenState>> GetPath()
        {
            var path = new List<State<TState, THiddenState>>();
            var cur = State;
            do
            {
                path.Add(cur);
                cur = cur.Parent;
            }
            while (cur != null);

            path.Reverse();
            return new ReadOnlyCollection<State<TState, THiddenState>>(path);
        }
    }
}