using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkflowsCore.StateMachines
{
    public class StateMachine<TState, THiddenState>
    {
        private readonly IDictionary<TState, State<TState, THiddenState>> _states =
            new Dictionary<TState, State<TState, THiddenState>>();

        private readonly IDictionary<THiddenState, State<TState, THiddenState>> _hiddenStates =
            new Dictionary<THiddenState, State<TState, THiddenState>>();

        public State<TState, THiddenState> ConfigureState(TState state)
        {
            State<TState, THiddenState> stateObj;
            if (_states.TryGetValue(state, out stateObj))
            {
                return stateObj;
            }

            stateObj = new State<TState, THiddenState>(this, state);
            _states.Add(state, stateObj);
            return stateObj;
        }

        public State<TState, THiddenState> ConfigureHiddenState(THiddenState state)
        {
            State<TState, THiddenState> stateObj;
            if (_hiddenStates.TryGetValue(state, out stateObj))
            {
                return stateObj;
            }

            stateObj = new State<TState, THiddenState>(this, state);
            _hiddenStates.Add(state, stateObj);
            return stateObj;
        }

        public Task Run(
            WorkflowBase workflow,
            StateId<TState, THiddenState> initialState,
            bool isRestoringState,
            Action<State<TState, THiddenState>> onStateChangedHandler = null)
        {
            throw new NotImplementedException();
        }
    }

    public class StateMachine<TState> : StateMachine<TState, string>
    {
    }
}
