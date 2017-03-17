﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorkflowsCore
{
    public abstract class WorkflowBase<TState> : WorkflowBase
    {
        private readonly int _fullStatesHistoryLimit;

        protected WorkflowBase(int fullStatesHistoryLimit = 100)
            : base(null)
        {
            _fullStatesHistoryLimit = fullStatesHistoryLimit;
        }

        protected WorkflowBase(Func<IWorkflowStateRepository> workflowRepoFactory, int fullStatesHistoryLimit = 100)
            : base(workflowRepoFactory)
        {
            _fullStatesHistoryLimit = fullStatesHistoryLimit;
        }

        protected internal event EventHandler<StateChangedEventArgs> StateChanged;

        [DataField(IsTransient = true)]
        protected internal TState State =>
            StatesHistory.Count == 0 ? default(TState) : StatesHistory[StatesHistory.Count - 1];

        [DataField(IsTransient = true)]
        protected internal TState PreviousState => 
            StatesHistory.Count != 2 ? default(TState) : StatesHistory[0];

        [DataField(IsTransient = true)]
        protected bool IsLoaded { get; private set; }

        [DataField]
        private IList<TState> StatesHistory { get; set; }

        [DataField]
        private IDictionary<TState, int> StatesStats { get; set; }

        /// <summary>
        /// It is stored for diagnosing purposes only
        /// </summary>
        [DataField]
        private IList<Tuple<TState, DateTime>> FullStatesHistory { get; set; }

        public Task<TState> GetStateAsync() => DoWorkflowTaskAsync(() => State);

        protected internal bool WasIn(TState state) => TimesIn(state) > 0;

        protected internal abstract void OnStatesInit();

        protected override void OnInit()
        {
            base.OnInit();
            StatesHistory = new List<TState>();
            FullStatesHistory = new List<Tuple<TState, DateTime>>();
            StatesStats = new Dictionary<TState, int>();
            OnStatesInit();
        }

        protected override void OnLoaded()
        {
            base.OnLoaded();
            if (!StatesHistory.Any())
            {
                return;
            }

            IsLoaded = true;
        }

        protected void SetState(TState state, bool isStateRestored = false)
        {
            UpdateFullStatesHistory(state);

            if (isStateRestored)
            {
                return;
            }

            UpdateStatesStats(state);
            UpdateStatesHistory(state);
            SaveWorkflowData();
            StateChanged?.Invoke(this, new StateChangedEventArgs(PreviousState, State));
        }

        protected int TimesIn(TState state)
        { // TODO: Add ability to clear stats
            int stats;
            return !StatesStats.TryGetValue(state, out stats) ? 0 : stats;
        }

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
            int stats;
            StatesStats.TryGetValue(state, out stats);
            StatesStats[state] = ++stats;
        }

        private void UpdateStatesHistory(TState state)
        {
            if (StatesHistory.Count < 2)
            {
                StatesHistory.Add(state);
                return;
            }

            StatesHistory[0] = StatesHistory[1];
            StatesHistory[1] = state;
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
    }
}
