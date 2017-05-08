using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkflowsCore.StateMachines
{
    public class StateMachine<TState, TInternalState>
    {
        private readonly IDictionary<TState, State<TState, TInternalState>> _states =
            new Dictionary<TState, State<TState, TInternalState>>();

        private readonly IDictionary<TInternalState, State<TState, TInternalState>> _internalStates =
            new Dictionary<TInternalState, State<TState, TInternalState>>();

        public IEnumerable<State<TState, TInternalState>> States => _states.Values;

        public IEnumerable<State<TState, TInternalState>> InternalStates => _internalStates.Values;

        public State<TState, TInternalState> ConfigureState(TState state)
        {
            State<TState, TInternalState> stateObj;
            if (_states.TryGetValue(state, out stateObj))
            {
                return stateObj;
            }

            stateObj = new State<TState, TInternalState>(this, state);
            _states.Add(state, stateObj);
            return stateObj;
        }

        public State<TState, TInternalState> ConfigureInternalState(TInternalState state)
        {
            State<TState, TInternalState> stateObj;
            if (_internalStates.TryGetValue(state, out stateObj))
            {
                return stateObj;
            }

            stateObj = new State<TState, TInternalState>(this, state);
            _internalStates.Add(state, stateObj);
            return stateObj;
        }

        public State<TState, TInternalState> ConfigureState(StateId<TState, TInternalState> state) => 
            !state.IsInternalState ? ConfigureState(state.Id) : ConfigureInternalState(state.InternalState);

        public StateMachineInstance Run(
            WorkflowBase workflow,
            StateId<TState, TInternalState> initialState,
            bool isRestoringState,
            Action<StateTransition<TState, TInternalState>> onStateChangedHandler = null)
        {
            workflow.EnsureWorkflowTaskScheduler();
            var state = GetState(initialState);
            return new StateMachineInstance(workflow, state, isRestoringState, onStateChangedHandler);
        }

        private State<TState, TInternalState> GetState(StateId<TState, TInternalState> stateId)
        {
            State<TState, TInternalState> res;

            if (!stateId.IsInternalState)
            {
                _states.TryGetValue(stateId.Id, out res);
            }
            else
            {
                _internalStates.TryGetValue(stateId.InternalState, out res);
            }

            if (res == null)
            {
                throw new ArgumentOutOfRangeException(nameof(stateId), $"State is not configured: {stateId}");
            }

            return res;
        }

        public class StateMachineInstance
        {
            private readonly Action<StateTransition<TState, TInternalState>> _onStateChangedHandler;
            private readonly Lazy<Task> _taskLazy;
            private State<TState, TInternalState>.StateInstance _stateInstance;

            internal StateMachineInstance(
                WorkflowBase workflow,
                State<TState, TInternalState> state,
                bool isRestoringState,
                Action<StateTransition<TState, TInternalState>> onStateChangedHandler)
            {
                _onStateChangedHandler = onStateChangedHandler;
                Workflow = workflow;

                // We do not start initial state transition here to avoid race conditions when instance of state machine in parent workflow
                // is not set yet but states started execution and some handler tries to execute actions on workflow
                _taskLazy = new Lazy<Task>(
                    () =>
                    {
                        workflow.EnsureWorkflowTaskScheduler();
                        return Run(state, isRestoringState);
                    },
                    false);
            }

            public WorkflowBase Workflow { get; set; }

            public Task Task => _taskLazy.Value;

            public bool IsActionAllowed(string action) => 
                StateExtensions.SetWorkflowTemporarily(Workflow, () => _stateInstance.IsActionAllowed(action)) ?? false;

            // ReSharper disable once FunctionNeverReturns
            private async Task Run(State<TState, TInternalState> initialState, bool isRestoringState)
            {
                var operation = await Workflow.WaitForReadyAndStartOperation();
                Workflow.ResetOperation();
                var intialTransition = new StateTransition<TState, TInternalState>(
                    initialState,
                    operation,
                    isRestoringState,
                    _onStateChangedHandler);
                _stateInstance = intialTransition.PerformTransition();

                while (true)
                {
                    var transition = await StateExtensions.SetWorkflowTemporarily(Workflow, () => _stateInstance.Task);
                    _stateInstance = transition.PerformTransition();
                }
            }
        }
    }

    public class StateMachine<TState> : StateMachine<TState, string>
    {
    }
}
