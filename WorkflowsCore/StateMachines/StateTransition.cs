using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace WorkflowsCore.StateMachines
{
    public class StateTransition<T>
    {
        public StateTransition(State<T> state, IDisposable workflowOperation, bool isRestoringState = false)
        {
            if (workflowOperation == null)
            {
                throw new ArgumentNullException(nameof(workflowOperation));
            }

            State = state;
            WorkflowOperation = workflowOperation;
            IsRestoringState = isRestoringState;
            Path = GetPath();
        }

        public StateTransition(State<T> state, StateTransition<T> transition)
            : this(state, transition.WorkflowOperation, transition.IsRestoringState)
        {
        }

        public State<T> State { get; }

        public IDisposable WorkflowOperation { get; }

        public IReadOnlyCollection<State<T>> Path { get; }

        public bool IsRestoringState { get; }

        public IList<State<T>> FindPathFrom(State<T> parentState)
        {
            var res = Path.SkipWhile(s => s != parentState).Skip(1).ToList();
            return res.Any() ? res : null;
        } 

        private IReadOnlyCollection<State<T>> GetPath()
        {
            var path = new List<State<T>>();
            var cur = State;
            do
            {
                path.Add(cur);
                cur = cur.Parent;
            }
            while (cur != null);

            path.Reverse();
            return new ReadOnlyCollection<State<T>>(path);
        }
    }
}