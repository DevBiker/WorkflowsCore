using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkflowsCore.StateMachines
{
    public class StateMachine<T>
    {
        private readonly IDictionary<T, State<T>> _states = new Dictionary<T, State<T>>();
        private readonly IDictionary<string, State<T>> _hiddenStates = new Dictionary<string, State<T>>();

        public State<T> ConfigureState(T state)
        {
            State<T> stateObj;
            if (_states.TryGetValue(state, out stateObj))
            {
                return stateObj;
            }

            stateObj = new State<T>(this, state);
            _states.Add(state, stateObj);
            return stateObj;
        }

        public State<T> ConfigureHiddenState(string name)
        {
            State<T> stateObj;
            if (_hiddenStates.TryGetValue(name, out stateObj))
            {
                return stateObj;
            }

            stateObj = new State<T>(this, name);
            _hiddenStates.Add(name, stateObj);
            return stateObj;
        }

        public Task Run(
            WorkflowBase workflow,
            State<T> initialState,
            bool isRestoringState,
            Action<State<T>> onStateChangedHandler = null)
        {
            throw new NotImplementedException();
        }
    }
}
