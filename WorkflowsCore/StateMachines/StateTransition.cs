using System;
using System.Collections.Generic;

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
        }

        public State<T> State { get; } 

        public IDisposable WorkflowOperation { get; }

        public IReadOnlyList<State<T>> Path { get; } = null; 

        public bool IsRestoringState { get; } = false;
    }
}