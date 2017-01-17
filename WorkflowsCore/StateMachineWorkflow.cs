using System;
using System.Threading.Tasks;
using WorkflowsCore.StateMachines;
using WorkflowsCore.Time;

namespace WorkflowsCore
{
    public abstract class StateMachineWorkflow<TState, THiddenState> : WorkflowBase<TState>
    {
        private readonly StateMachine<TState, THiddenState> _stateMachine = new StateMachine<TState, THiddenState>();
        private readonly TaskCompletionSource<bool> _completeWorkflowTcs = new TaskCompletionSource<bool>();
        private StateId<TState, THiddenState> _initialState;
        private StateMachine<TState, THiddenState>.StateMachineInstance _instance;

        protected StateMachineWorkflow(int fullStatesHistoryLimit = 100) 
            : base(fullStatesHistoryLimit)
        {
        }

        protected StateMachineWorkflow(
            Func<IWorkflowStateRepository> workflowRepoFactory,
            int fullStatesHistoryLimit = 100) 
            : base(workflowRepoFactory, fullStatesHistoryLimit)
        {
        }

        protected internal override bool IsActionAllowed(string action) => 
            _instance?.IsActionAllowed(action) ?? base.IsActionAllowed(action);

        protected State<TState, THiddenState> ConfigureState(TState state)
        {
            base.ConfigureState(state);
            return _stateMachine.ConfigureState(state);
        }

        protected State<TState, THiddenState> ConfigureHiddenState(THiddenState state) => 
            _stateMachine.ConfigureState(state);

        protected void SetInitialState(TState state) => _initialState = state;

        protected override Task RunAsync()
        {
            if (IsRestoringState)
            {
                SetInitialState(TransientStatesHistory[TransientStatesHistory.Count - 1]);
            }

            return this.WaitForAny(
                () => Task.WhenAny(_completeWorkflowTcs.Task, this.WaitForDate(DateTime.MaxValue)),
                RunStateMachine);
        }

        protected void CompleteWorkflow() => _completeWorkflowTcs.TrySetResult(true);

        private Task RunStateMachine()
        {
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
        protected StateMachineWorkflow(int fullStatesHistoryLimit = 100) 
            : base(fullStatesHistoryLimit)
        {
        }

        protected StateMachineWorkflow(
            Func<IWorkflowStateRepository> workflowRepoFactory,
            int fullStatesHistoryLimit = 100)
            : base(workflowRepoFactory, fullStatesHistoryLimit)
        {
        }
    }
}
