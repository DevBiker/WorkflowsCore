using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WorkflowsCore
{
    public abstract class WorkflowBase<TState> : WorkflowBase
    {
        private readonly int _fullStatesHistoryLimit;
        private readonly IDictionary<TState, StateDefinition> _stateDefinitions = new Dictionary<TState, StateDefinition>();

        protected WorkflowBase(Func<IWorkflowStateRepository> workflowRepoFactory, int fullStatesHistoryLimit = 100)
            : base(workflowRepoFactory, false)
        {
            _fullStatesHistoryLimit = fullStatesHistoryLimit;
        }

        protected WorkflowBase(
            Func<IWorkflowStateRepository> workflowRepoFactory,
            CancellationToken parentCancellationToken,
            int fullStatesHistoryLimit = 100)
            : base(workflowRepoFactory, false, parentCancellationToken)
        {
            _fullStatesHistoryLimit = fullStatesHistoryLimit;
        }

        protected internal event EventHandler<StateChangedEventArgs> StateChanged;

        internal bool IsRestoringState
        {
            get { return GetTransientData<bool>(nameof(IsRestoringState)); }

            set { SetTransientData(nameof(IsRestoringState), value); }
        }

        internal IList<TState> TransientStatesHistory => GetTransientData<IList<TState>>(nameof(StatesHistory));

        protected internal TState State
        {
            get { return GetTransientData<TState>(nameof(State)); }

            set { SetTransientData(nameof(State), value); }
        }

        protected internal TState PreviousState => 
            StatesHistory.Count == 0 ? default(TState) : StatesHistory[StatesHistory.Count - 1];

        private IList<TState> StatesHistory => GetData<IList<TState>>(nameof(StatesHistory));

        private IDictionary<TState, StateStats> StatesStats => 
            GetData<IDictionary<TState, StateStats>>(nameof(StatesStats));

        /// <summary>
        /// It is stored for diagnosing purposes only
        /// </summary>
        private IList<Tuple<TState, DateTime>> FullStatesHistory => 
            GetData<IList<Tuple<TState, DateTime>>>(nameof(FullStatesHistory));

        public Task<TState> GetStateAsync() => DoWorkflowTaskAsync(() => State);

        protected internal bool WasIn(TState state, bool ignoreSuppression = false) =>
            TimesIn(state, ignoreSuppression) > 0;

        protected override void OnInit()
        {
            base.OnInit();
            SetData(nameof(StatesHistory), new List<TState>());
            SetData(nameof(FullStatesHistory), new List<Tuple<TState, DateTime>>());
            SetData(nameof(StatesStats), new Dictionary<TState, StateStats>());
            OnStatesInit();
        }

        protected override void OnLoaded()
        {
            base.OnLoaded();
            if (!StatesHistory.Any())
            {
                SetStateInitialized();
                return;
            }

            IsRestoringState = true;
            SetTransientData(nameof(StatesHistory), StatesHistory);
            SetData(nameof(StatesHistory), new List<TState>());
        }

        protected abstract void OnStatesInit();

        protected override bool IsActionAllowed(string action) =>
            _stateDefinitions[State].AllowedActions.Intersect(GetActionSynonyms(action)).Any();

        protected void ConfigureState(
            TState state,
            Action onStateHandler = null,
            IEnumerable<TState> suppressStates = null,
            bool suppressAll = false,
            IEnumerable<string> availableActions = null)
        { // TODO: Check availableActions are all configured actions
            ConfigureState(
                state,
                isRestoringState =>
                {
                    if (!isRestoringState)
                    {
                        onStateHandler?.Invoke();
                    }
                },
                suppressStates,
                suppressAll,
                availableActions);
        }

        protected void ConfigureState(
            TState state,
            Action<bool> onStateHandler,
            IEnumerable<TState> suppressStates = null,
            bool suppressAll = false,
            IEnumerable<string> availableActions = null)
        {
            StateDefinition existing;
            if (_stateDefinitions.TryGetValue(state, out existing))
            {
                throw new InvalidOperationException();
            }

            _stateDefinitions[state] = new StateDefinition
            {
                Handler = onStateHandler,
                SuppressStates = suppressStates?.ToList() ?? new List<TState>(),
                SuppressAll = suppressAll,
                AllowedActions = availableActions?.ToList() ?? new List<string>()
            };
        }

        protected void SetState(TState state)
        {
            // TODO: Disallow calling SetState() from within SetState()
            StateDefinition stateDefinition;
            if (!_stateDefinitions.TryGetValue(state, out stateDefinition))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            var setStateAsInitialized = false;
            if (!IsRestoringState)
            {
                setStateAsInitialized = true;
            }
            else
            {
                if (!EqualityComparer<TState>.Default.Equals(state, TransientStatesHistory[0]))
                {
                    IsRestoringState = false;
                }
                else
                {
                    TransientStatesHistory.RemoveAt(0);
                }
            }

            var oldState = State;
            State = state;
            UpdateStatesStats(state);
            UpdateFullStatesHistory(state);
            stateDefinition.Handler(IsRestoringState);
            UpdateStatesStatsAndHistory(state, stateDefinition);
            if (!IsRestoringState)
            {
                SaveWorkflowData();
            }

            StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, State));
            if (IsRestoringState)
            {
                IsRestoringState = TransientStatesHistory.Any();
            }

            if (setStateAsInitialized)
            {
                SetStateInitialized();
            }
            else if (!IsRestoringState)
            {
                SetTransientData(nameof(StatesHistory), (IList<TState>)null);
                SetStateInitialized();
            }
        }

        protected int TimesIn(TState state, bool ignoreSuppression = false)
        { // TODO: Add ability to clear stats
            StateStats stats;
            if (!StatesStats.TryGetValue(state, out stats))
            {
                return 0;
            }

            return !ignoreSuppression ? stats.EnteredCounter : stats.IgnoreSuppressionEnteredCounter;
        }

        private new void SetStateInitialized() => base.SetStateInitialized();

        private void UpdateFullStatesHistory(TState state)
        {
            FullStatesHistory.Add(Tuple.Create(state, TimeProvider.Now));
            while (FullStatesHistory.Count > _fullStatesHistoryLimit)
            {
                FullStatesHistory.RemoveAt(0);
            }
        }

        private void UpdateStatesStats(TState state)
        {
            if (IsRestoringState)
            {
                return;
            }

            StateStats stats;
            if (!StatesStats.TryGetValue(state, out stats))
            {
                stats = new StateStats();
                StatesStats.Add(state, stats);
            }

            ++stats.EnteredCounter;
            ++stats.IgnoreSuppressionEnteredCounter;
        }

        private void UpdateStatesStatsAndHistory(TState state, StateDefinition stateDefinition)
        {
            if (stateDefinition.SuppressAll)
            {
                StatesHistory.Clear();
                if (!IsRestoringState)
                {
                    var otherStatesStats = StatesStats.Where(p => !EqualityComparer<TState>.Default.Equals(p.Key, state));
                    foreach (var stats in otherStatesStats)
                    {
                        stats.Value.EnteredCounter = 0;
                    }
                }
            }

            if (stateDefinition.SuppressStates.Any(s => EqualityComparer<TState>.Default.Equals(s, PreviousState)))
            {
                if (!IsRestoringState && StatesStats.ContainsKey(PreviousState))
                {
                    StatesStats[PreviousState].EnteredCounter = 0;
                }

                StatesHistory[StatesHistory.Count - 1] = state;
                return;
            }

            if (EqualityComparer<TState>.Default.Equals(state, PreviousState))
            {
                return;
            }

            StatesHistory.Add(state);
        }

        protected internal class StateChangedEventArgs : EventArgs
        {
            public StateChangedEventArgs(TState oldState, TState newState)
            {
                OldState = oldState;
                NewState = newState;
            }

            public TState OldState { get; }

            public TState NewState { get; }
        }

        private class StateDefinition
        {
            public Action<bool> Handler { get; set; }

            public IList<TState> SuppressStates { get; set; }

            public bool SuppressAll { get; set; }

            public IList<string> AllowedActions { get; set; } 
        }
    }
}
