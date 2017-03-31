using System;
using System.Collections.Generic;
using System.Linq;
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

        [DataField]
        protected IList<THiddenState> HiddenState { get; private set; } = new THiddenState[0];

        [DataField(IsTransient = true)]
        private State<TState, THiddenState>.StateInstance NonHiddenAncestorStateInstance { get; set; }

        protected internal override bool IsActionAllowed(string action) => 
            _instance?.IsActionAllowed(action) ?? base.IsActionAllowed(action);

        protected State<TState, THiddenState> ConfigureState(TState state) => StateMachine.ConfigureState(state);

        protected State<TState, THiddenState> ConfigureHiddenState(THiddenState state) => 
            StateMachine.ConfigureState(state);

        protected void SetInitialState(StateId<TState, THiddenState> state) => _initialState = state;

        protected override Task RunAsync()
        {
            if (IsLoaded)
            {
                if (HiddenState.Any())
                {
                    SetInitialState(HiddenState.Single());
                }
                else
                {
                    SetInitialState(State);
                }
            }

            return this.WaitForAny(
                () => Task.WhenAny(_completeWorkflowTcs.Task, this.WaitForDate(DateTime.MaxValue)),
                RunStateMachine);
        }

        protected void CompleteWorkflow() => _completeWorkflowTcs.TrySetResult(true);

        protected virtual void OnStateChanged(StateTransition<TState, THiddenState> stateTransition)
        {
            if (!stateTransition.State.StateId.IsHiddenState)
            {
                NonHiddenAncestorStateInstance = stateTransition.StateInstance;
                HiddenState = new THiddenState[0];
                SetState((TState)stateTransition.State.StateId, stateTransition.IsRestoringState);
            }
            else
            {
                var hiddenState = (THiddenState)stateTransition.State.StateId;
                HiddenState = new[] { hiddenState };
                var nonHiddenAncestor = stateTransition.StateInstance.GetNonHiddenAncestor();

                if (nonHiddenAncestor == null)
                {
                    throw new InvalidOperationException(
                        $"Cannot transition to hidden state {hiddenState} without non-hidden parent");
                }

                if (NonHiddenAncestorStateInstance != nonHiddenAncestor)
                {
                    NonHiddenAncestorStateInstance = nonHiddenAncestor;
                    SetState((TState)nonHiddenAncestor.State.StateId, stateTransition.IsRestoringState);
                }
            }
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
