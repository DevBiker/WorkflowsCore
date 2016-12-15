using System.Threading.Tasks;
using WorkflowsCore.StateMachines;

namespace WorkflowsCore
{
    public abstract class StateMachineWorkflow<TState, THiddenState> : WorkflowBase<TState>
    {
        private readonly StateMachine<TState, THiddenState> _stateMachine = new StateMachine<TState, THiddenState>();
        private StateId<TState, THiddenState> _initialState;
        private StateMachine<TState, THiddenState>.StateMachineInstance _instance;

        protected internal override bool IsActionAllowed(string action) => _instance.IsActionAllowed(action);

        protected State<TState, THiddenState> ConfigureState(TState state)
        {
            base.ConfigureState(state);
            return _stateMachine.ConfigureState(state);
        }

        protected void SetInitialState(TState state) => _initialState = state;

        protected override Task RunAsync()
        {
            if (IsRestoringState)
            {
                SetInitialState(TransientStatesHistory[TransientStatesHistory.Count - 1]);
            }

            _instance = _stateMachine.Run(this, _initialState, IsRestoringState, OnStateChangedHandler);
            return _instance.Task;
        }

        private void OnStateChangedHandler(StateTransition<TState, THiddenState> stateTransition)
        {
            SetState((TState)stateTransition.State.StateId); // TODO: Support inner hidden states
        }
    }

    public abstract class StateMachineWorkflow<TState> : StateMachineWorkflow<TState, string>
    {
    }
}
