using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WorkflowsCore.StateMachines
{
    public class StateTransition<T>
    {
        public StateTransition(State<T> state, IDisposable workflowOperation)
        {
            if (workflowOperation == null)
            {
                throw new ArgumentNullException(nameof(workflowOperation));
            }

            State = state;
            WorkflowOperation = workflowOperation;
            Path = GetPath();
        }

        public State<T> State { get; }

        public IDisposable WorkflowOperation { get; }

        public IReadOnlyCollection<State<T>> Path { get; }

        public bool IsRestoringState { get; } = false;

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