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

        public StateMachineInstance Run(
            WorkflowBase workflow,
            StateId<TState, THiddenState> initialState,
            bool isRestoringState,
            Action<StateTransition<TState, THiddenState>> onStateChangedHandler = null)
        {
            workflow.EnsureWorkflowTaskScheduler();
            var state = GetState(initialState);
            return new StateMachineInstance(workflow, state, isRestoringState, onStateChangedHandler);
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

        public class StateMachineInstance
        {
            private readonly Action<StateTransition<TState, THiddenState>> _onStateChangedHandler;
            private State<TState, THiddenState>.StateInstance _stateInstance;

            internal StateMachineInstance(
                WorkflowBase workflow,
                State<TState, THiddenState> state,
                bool isRestoringState,
                Action<StateTransition<TState, THiddenState>> onStateChangedHandler)
            {
                _onStateChangedHandler = onStateChangedHandler;
                Workflow = workflow;
                Task = Run(state, isRestoringState);
            }

            public WorkflowBase Workflow { get; set; }

            public Task Task { get; }

            public bool IsActionAllowed(string action) => _stateInstance.IsActionAllowed(action) ?? false;

            // ReSharper disable once FunctionNeverReturns
            private async Task Run(State<TState, THiddenState> initialState, bool isRestoringState)
            {
                var operation = await Workflow.WaitForReadyAndStartOperation();
                _stateInstance = StateExtensions.SetWorkflowTemporarily(
                    Workflow,
                    () => initialState.Run(
                        new StateTransition<TState, THiddenState>(
                            initialState,
                            operation,
                            isRestoringState,
                            _onStateChangedHandler)));

                while (true)
                {
                    var transition = await _stateInstance.Task;
                    _stateInstance = StateExtensions.SetWorkflowTemporarily(
                        Workflow,
                        () => transition.Path.First().Run(transition));
                }
            }
        }
    }

    public class StateMachine<TState> : StateMachine<TState, string>
    {
    }
}
