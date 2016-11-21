using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorkflowsCore
{
    public abstract class WorkflowBase<TState> : WorkflowBase
    {
        private readonly int _fullStatesHistoryLimit;

        private readonly IDictionary<string, StateCategoryDefinition> _stateCategoryDefinitions =
            new Dictionary<string, StateCategoryDefinition>();

        private readonly IDictionary<TState, StateDefinition> _stateDefinitions = new Dictionary<TState, StateDefinition>();

        protected WorkflowBase(int fullStatesHistoryLimit = 100)
            : base(null, false)
        {
            _fullStatesHistoryLimit = fullStatesHistoryLimit;
        }

        protected WorkflowBase(Func<IWorkflowStateRepository> workflowRepoFactory, int fullStatesHistoryLimit = 100)
            : base(workflowRepoFactory, false)
        {
            _fullStatesHistoryLimit = fullStatesHistoryLimit;
        }

        protected internal event EventHandler<StateChangedEventArgs> StateChanged;

        [DataField(IsTransient = true)]
        internal bool IsRestoringState { get; set; }

        [DataField(IsTransient = true)]
        internal IList<TState> TransientStatesHistory { get; set; }

        [DataField(IsTransient = true)]
        protected internal TState State { get; set; }

        [DataField(IsTransient = true)]
        protected internal TState PreviousState => 
            StatesHistory.Count == 0 ? default(TState) : StatesHistory[StatesHistory.Count - 1];

        [DataField]
        private IList<TState> StatesHistory { get; set; }

        [DataField]
        private IDictionary<TState, StateStats> StatesStats { get; set; }

        /// <summary>
        /// It is stored for diagnosing purposes only
        /// </summary>
        [DataField]
        private IList<Tuple<TState, DateTime>> FullStatesHistory { get; set; }

        public Task<TState> GetStateAsync() => DoWorkflowTaskAsync(() => State);

        protected internal bool WasIn(TState state, bool ignoreSuppression = false) =>
            TimesIn(state, ignoreSuppression) > 0;

        protected override void OnInit()
        {
            base.OnInit();
            StatesHistory = new List<TState>();
            FullStatesHistory = new List<Tuple<TState, DateTime>>();
            StatesStats = new Dictionary<TState, StateStats>();
            OnStatesInit();
        }

        protected override void OnLoaded()
        {
            base.OnLoaded();
            if (!StatesHistory.Any())
            {
                return;
            }

            IsRestoringState = true;
            TransientStatesHistory = StatesHistory;
            StatesHistory = new List<TState>();
        }

        protected abstract void OnStatesInit();

        protected override bool IsActionAllowed(string action) =>
            _stateDefinitions[State].AllowedActions.Intersect(GetActionSynonyms(action)).Any();

        protected void ConfigureStateCategory(string categoryName = null, IEnumerable<string> availableActions = null)
        {
            categoryName = categoryName ?? string.Empty;
            StateCategoryDefinition stateCategoryDefinition;
            if (_stateCategoryDefinitions.TryGetValue(categoryName, out stateCategoryDefinition))
            {
                throw new InvalidOperationException();
            }

            _stateCategoryDefinitions[categoryName] = new StateCategoryDefinition
            {
                AllowedActions = availableActions?.ToList() ?? new List<string>()
            };
        }
        
        protected void ConfigureState(
            TState state,
            Action onStateHandler = null,
            IEnumerable<TState> suppressStates = null,
            bool suppressAll = false,
            IEnumerable<string> availableActions = null,
            IEnumerable<string> disallowedActions = null,
            IEnumerable<string> stateCategories = null)
        {
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
                availableActions,
                disallowedActions,
                stateCategories);
        }

        protected void ConfigureState(
            TState state,
            Action<bool> onStateHandler,
            IEnumerable<TState> suppressStates = null,
            bool suppressAll = false,
            IEnumerable<string> availableActions = null,
            IEnumerable<string> disallowedActions = null,
            IEnumerable<string> stateCategories = null)
        {
            StateDefinition existing;
            if (_stateDefinitions.TryGetValue(state, out existing))
            {
                throw new InvalidOperationException();
            }

            var allowedActions = GetStateCategoriesAllowedActions(stateCategories ?? Enumerable.Empty<string>())
                .Union(availableActions ?? Enumerable.Empty<string>())
                .Except(disallowedActions ?? Enumerable.Empty<string>())
                .ToList();
            EnsureActionsConfigured(allowedActions);

            _stateDefinitions[state] = new StateDefinition
            {
                Handler = onStateHandler,
                SuppressStates = suppressStates?.ToList() ?? new List<TState>(),
                SuppressAll = suppressAll,
                AllowedActions = allowedActions
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
            if (!IsRestoringState && StateInitializedTask.Status == TaskStatus.RanToCompletion)
            {
                SaveWorkflowData();
            }

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
                TransientStatesHistory = null;
                SetStateInitialized();
            }

            StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, State));
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

        private IEnumerable<string> GetStateCategoriesAllowedActions(IEnumerable<string> stateCategories) =>
            stateCategories.Aggregate(
                GetStateCategoryAllowedActions(),
                (r, c) => r.Union(GetStateCategoryAllowedActions(c)));

        private IEnumerable<string> GetStateCategoryAllowedActions(string categoryName = null)
        {
            categoryName = categoryName ?? string.Empty;
            StateCategoryDefinition stateCategoryDefinition;
            if (!_stateCategoryDefinitions.TryGetValue(categoryName, out stateCategoryDefinition))
            {
                if (categoryName != string.Empty)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(categoryName),
                        $"State category {categoryName} is not configured");
                }

                return Enumerable.Empty<string>();
            }

            return stateCategoryDefinition.AllowedActions;
        }

        private void EnsureActionsConfigured(IEnumerable<string> actions)
        {
            foreach (var action in actions)
            {
                GetActionMetadata(action);
            }
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

        private class StateCategoryDefinition
        {
            public IList<string> AllowedActions { get; set; }
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
