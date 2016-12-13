using System;
using System.Collections.Generic;
using System.Linq;
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

        // ReSharper disable once FunctionNeverReturns
        public async Task Run(
            WorkflowBase workflow,
            StateId<TState, THiddenState> initialState,
            bool isRestoringState,
            Action<State<TState, THiddenState>> onStateChangedHandler = null)
        {
            var state = GetState(initialState);
            var operation = await workflow.WaitForReadyAndStartOperation();
            var stateInstance = StateExtensions.SetWorkflowTemporarily(
                workflow,
                () => state.Run(
                    new StateTransition<TState, THiddenState>(
                        state,
                        operation,
                        isRestoringState,
                        onStateChangedHandler)));

            while (true)
            {
                var transition = await stateInstance.Task;
                stateInstance = StateExtensions.SetWorkflowTemporarily(
                    workflow,
                    () => transition.Path.First().Run(transition));
            }
        }

        private State<TState, THiddenState> GetState(StateId<TState, THiddenState> stateId)
        {
            State<TState, THiddenState> res;

            if (!stateId.IsHiddenState)
            {
                _states.TryGetValue(stateId.Id, out res);
            }
            else
            {
                _hiddenStates.TryGetValue(stateId.HiddenId, out res);
            }

            if (res == null)
            {
                throw new ArgumentOutOfRangeException(nameof(stateId), $"State is not configured: {stateId}");
            }

            return res;
        }
    }

    public class StateMachine<TState> : StateMachine<TState, string>
    {
    }
}
