using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WorkflowsCore.StateMachines;
using WorkflowsCore.Time;

namespace WorkflowsCore
{
    public abstract class StateMachineWorkflow<TState, TInternalState> : WorkflowBase<TState>
    {
        public const string InnerInternalStateChangedEvent = "InnerInternalStateChanged";
        public const string InnerInternalStateRestoredEvent = "InnerInternalStateRestored";

        private readonly TaskCompletionSource<bool> _completeWorkflowTcs = new TaskCompletionSource<bool>();
        private StateId<TState, TInternalState> _initialState;
        private StateMachine<TState, TInternalState>.StateMachineInstance _instance;

        protected StateMachineWorkflow(int eventLogLimit = 100)
            : base(eventLogLimit)
        {
        }

        protected StateMachineWorkflow(
            Func<IWorkflowStateRepository> workflowRepoFactory,
            int eventLogLimit = 100)
            : base(workflowRepoFactory, eventLogLimit)
        {
        }

        internal StateMachine<TState, TInternalState> StateMachine { get; } = new StateMachine<TState, TInternalState>();

        [DataField]
        protected IList<TInternalState> InternalState { get; private set; } = new TInternalState[0];

        [DataField(IsTransient = true)]
        private State<TState, TInternalState>.StateInstance NonInternalAncestorStateInstance { get; set; }

        protected internal override bool IsActionAllowed(string action, NamedValues parameters) =>
            _instance?.IsActionAllowed(action) ?? false;

        protected State<TState, TInternalState> ConfigureState(TState state, bool isHidden = false) =>
            StateMachine.ConfigureState(state).Hide(isHidden);

        protected State<TState, TInternalState> ConfigureInternalState(TInternalState state, bool isHidden = false) =>
            StateMachine.ConfigureState(state).Hide(isHidden);

        protected void SetInitialState(StateId<TState, TInternalState> state) => _initialState = state;

        protected override Task RunAsync()
        {
            if (IsLoaded)
            {
                if (InternalState.Any())
                {
                    SetInitialState(InternalState.Single());
                }
                else
                {
                    SetInitialState(State);
                }
            }

            return this.WaitForAny(
                () => Task.WhenAny(_completeWorkflowTcs.Task, this.WaitForDate(DateTimeOffset.MaxValue)),
                RunStateMachine);
        }

        protected void CompleteWorkflow() => _completeWorkflowTcs.TrySetResult(true);

        protected virtual void OnStateChanged(StateTransition<TState, TInternalState> stateTransition)
        {
            if (!stateTransition.State.StateId.IsInternalState)
            {
                NonInternalAncestorStateInstance = stateTransition.StateInstance;
                if (InternalState.Any())
                {
                    InternalState = new TInternalState[0];
                    LogInnerInternalStateChanged(null, stateTransition.IsRestoringState);
                }

                SetState((TState)stateTransition.State.StateId, stateTransition.IsRestoringState);
            }
            else
            {
                var internalState = (TInternalState)stateTransition.State.StateId;
                InternalState = new[] { internalState };
                LogInnerInternalStateChanged(internalState, stateTransition.IsRestoringState);
                var internalAncestor = stateTransition.StateInstance.GetNonInternalAncestor();

                if (internalAncestor == null)
                {
                    throw new InvalidOperationException(
                        $"Cannot transition to internal state {internalState} without non-internal parent");
                }

                if (NonInternalAncestorStateInstance != internalAncestor)
                {
                    NonInternalAncestorStateInstance = internalAncestor;
                    SetState((TState)internalAncestor.State.StateId, stateTransition.IsRestoringState);
                }
                else if (!stateTransition.IsRestoringState)
                {
                    SaveWorkflowData();
                }
            }
        }

        private Task RunStateMachine()
        {
            _instance = StateMachine.Run(this, _initialState, IsLoaded, OnStateChanged);
            return _instance.Task;
        }

        private void LogInnerInternalStateChanged(StateId<TState, TInternalState>? stateId, bool isRestoringState)
        {
            LogEvent(
                !isRestoringState ? InnerInternalStateChangedEvent : InnerInternalStateRestoredEvent,
                new Dictionary<string, object> { ["State"] = !stateId.HasValue ? (object)null : stateId.Value.InternalState });
        }
    }

    public abstract class StateMachineWorkflow<TState> : StateMachineWorkflow<TState, string>
    {
        protected StateMachineWorkflow(int eventLogLimit = 100)
            : base(eventLogLimit)
        {
        }

        protected StateMachineWorkflow(
            Func<IWorkflowStateRepository> workflowRepoFactory,
            int eventLogLimit = 100)
            : base(workflowRepoFactory, eventLogLimit)
        {
        }
    }
}
