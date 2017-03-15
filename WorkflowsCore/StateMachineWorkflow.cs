using System;
using System.Threading.Tasks;
using WorkflowsCore.StateMachines;
using WorkflowsCore.Time;

namespace WorkflowsCore
{
    public abstract class StateMachineWorkflow<TState, THiddenState> : WorkflowBase<TState>
    {
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

        internal StateMachine<TState, THiddenState> StateMachine { get; } = new StateMachine<TState, THiddenState>();

        protected internal override bool IsActionAllowed(string action) => 
            _instance?.IsActionAllowed(action) ?? base.IsActionAllowed(action);

        protected State<TState, THiddenState> ConfigureState(TState state) => StateMachine.ConfigureState(state);

        protected State<TState, THiddenState> ConfigureHiddenState(THiddenState state) => 
            StateMachine.ConfigureState(state);

        protected void SetInitialState(TState state) => _initialState = state;

        protected override Task RunAsync()
        {
            if (IsLoaded)
            {
                SetInitialState(State);
            }

            return this.WaitForAny(
                () => Task.WhenAny(_completeWorkflowTcs.Task, this.WaitForDate(DateTime.MaxValue)),
                RunStateMachine);
        }

        protected void CompleteWorkflow() => _completeWorkflowTcs.TrySetResult(true);

        protected virtual void OnStateChanged(StateTransition<TState, THiddenState> stateTransition)
        {
            SetState((TState)stateTransition.State.StateId, stateTransition.IsRestoringState); // TODO: Support inner hidden states
        }

        private Task RunStateMachine()
        {
            _instance = StateMachine.Run(this, _initialState, IsLoaded, OnStateChanged);
            return _instance.Task;
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
