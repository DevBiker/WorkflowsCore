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
            Action<State<TState, THiddenState>> onStateChangedHandler = null)
        {
            if (workflowOperation == null)
            {
                throw new ArgumentNullException(nameof(workflowOperation));
            }

            State = state;
            _workflowOperation = workflowOperation;
            IsRestoringState = isRestoringState;
            OnStateChangedHandler = onStateChangedHandler;
            Path = GetPath();
        }

        public StateTransition(State<TState, THiddenState> state, StateTransition<TState, THiddenState> transition)
            : this(state, transition._workflowOperation, transition.IsRestoringState, transition.OnStateChangedHandler)
        {
        }

        public State<TState, THiddenState> State { get; }

        public IReadOnlyCollection<State<TState, THiddenState>> Path { get; }

        public bool IsRestoringState { get; }

        public Action<State<TState, THiddenState>> OnStateChangedHandler { get; }

        public void CompleteTransition()
        {
            OnStateChangedHandler?.Invoke(State);
            _workflowOperation.Dispose();
        }

        public IList<State<TState, THiddenState>> FindPathFrom(State<TState, THiddenState> parentState)
        {
            var res = Path.SkipWhile(s => s != parentState).Skip(1).ToList();
            return res.Any() ? res : null;
        }

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