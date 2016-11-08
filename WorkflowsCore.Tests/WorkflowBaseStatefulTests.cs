using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using WorkflowsCore.Time;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowBaseStatefulTests
    {
        private readonly WorkflowRepository _workflowRepo;
        private TestWorkflow _workflow;

        public WorkflowBaseStatefulTests()
        {
            _workflowRepo = new WorkflowRepository();
            Utilities.TimeProvider = new TestingTimeProvider();
            _workflow = new TestWorkflow(() => _workflowRepo);
        }

        private enum States
        {
            // ReSharper disable once UnusedMember.Local
            None,
            Outstanding,
            Due,
            Contacted
        }

        [Fact]
        public void IfStateIsConfiguredSecondTimeIoeShouldBeThrown()
        {
            var ex = Record.Exception(() => _workflow.ConfigureState(States.Outstanding, () => { }));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void IfStateBeingSetIsNotConfiguredThenAooreShouldBeThrown()
        {
            var ex = Record.Exception(() => _workflow.SetState(States.None));

            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        [Fact]
        public void SetStateShouldUpdateStateAndCallStateHandler()
        {
            Assert.NotEqual(States.Outstanding, _workflow.State);
            _workflow.SetState(States.Outstanding);
            Assert.Equal(States.Outstanding, _workflow.State);
            Assert.Equal(1, _workflow.OnOutstandingStateCounter);
            Assert.Equal(States.None, _workflow.OldState);
            Assert.Equal(States.Outstanding, _workflow.NewState);
        }

        [Fact]
        public void SetStateShouldSaveWorkflowDataAfterStateHandlerIsInvoked()
        {
            _workflow.SetState(States.Outstanding);
            Assert.Equal(1, _workflowRepo.SaveWorkflowDataCounter);
        }

        [Fact]
        public void SetStateShouldUpdateStatesHistory()
        {
            _workflow.SetState(States.Outstanding);
            Assert.True(new List<States> { States.Outstanding }.SequenceEqual(_workflow.StatesHistory));
        }

        [Fact]
        public void SetStateShouldUpdateStatesHistoryIfCurrentStateIsReentered()
        {
            _workflow.SetState(States.Due);
            _workflow.SetState(States.Due);
            Assert.True(new List<States> { States.Due }.SequenceEqual(_workflow.StatesHistory));
        }

        [Fact]
        public void SetStateShouldRemoveAllPreviousHistoryForSuppressAllStates()
        {
            _workflow.SetState(States.Outstanding);
            _workflow.SetState(States.Due);
            _workflow.SetState(States.Outstanding);
            Assert.True(new List<States> { States.Outstanding }.SequenceEqual(_workflow.StatesHistory));
        }

        [Fact]
        public void SetStateShouldRemoveAllPreviousStateFromHistoryIfItIsSuppressedByNewState()
        {
            _workflow.SetState(States.Outstanding);
            _workflow.SetState(States.Due);
            _workflow.SetState(States.Contacted);
            Assert.True(
                new List<States> { States.Outstanding, States.Contacted }.SequenceEqual(_workflow.StatesHistory));
        }

        [Fact]
        public void SetStateShouldAddStateToFullStatesHistory()
        {
            var now = TestingTimeProvider.Current.Now;
            _workflow.SetState(States.Outstanding);
            Assert.Equal(1, _workflow.FullStatesHistory.Count);
            Assert.Equal(States.Outstanding, _workflow.FullStatesHistory[0].Item1);
            Assert.Equal(now, _workflow.FullStatesHistory[0].Item2);

            now = TestingTimeProvider.Current.SetCurrentTime(now.AddMinutes(1));
            _workflow.SetState(States.Outstanding);
            Assert.Equal(2, _workflow.FullStatesHistory.Count);
            Assert.Equal(States.Outstanding, _workflow.FullStatesHistory[1].Item1);
            Assert.Equal(now, _workflow.FullStatesHistory[1].Item2);

            now = TestingTimeProvider.Current.SetCurrentTime(now.AddHours(1));
            _workflow.SetState(States.Due);

            now = TestingTimeProvider.Current.SetCurrentTime(now.AddMinutes(1));
            _workflow.SetState(States.Contacted);
            Assert.Equal(4, _workflow.FullStatesHistory.Count);
            Assert.Equal(States.Contacted, _workflow.FullStatesHistory[3].Item1);
            Assert.Equal(now, _workflow.FullStatesHistory[3].Item2);
        }

        [Fact]
        public void InFullStatesHistoryShouldBeStoredSpecifiedNumberOfItemAtMaximum()
        {
            _workflow.SetState(States.Outstanding);
            _workflow.SetState(States.Due);
            _workflow.SetState(States.Outstanding);
            _workflow.SetState(States.Due);
            _workflow.SetState(States.Contacted);
            Assert.Equal(4, _workflow.FullStatesHistory.Count);
            Assert.Equal(States.Due, _workflow.FullStatesHistory[0].Item1);
            Assert.Equal(States.Contacted, _workflow.FullStatesHistory[3].Item1);
        }

        [Fact]
        public void SetStateShouldAddStateStats()
        {
            _workflow.SetState(States.Outstanding);
            Assert.Equal(true, _workflow.WasIn(States.Outstanding));
            Assert.Equal(1, _workflow.TimesIn(States.Outstanding));

            _workflow.SetState(States.Outstanding);
            Assert.Equal(true, _workflow.WasIn(States.Outstanding));
            Assert.Equal(2, _workflow.TimesIn(States.Outstanding));
        }

        [Fact]
        public void IfIgnoreSuppressionSpecifiedThenStateStatsShouldIgnoreStateSuppression()
        {
            _workflow.SetState(States.Due);
            Assert.Equal(true, _workflow.WasIn(States.Due, ignoreSuppression: true));
            Assert.Equal(1, _workflow.TimesIn(States.Due, ignoreSuppression: true));

            _workflow.SetState(States.Outstanding);
            Assert.Equal(false, _workflow.WasIn(States.Due));
            Assert.Equal(0, _workflow.TimesIn(States.Due));
            Assert.Equal(true, _workflow.WasIn(States.Due, ignoreSuppression: true));
            Assert.Equal(1, _workflow.TimesIn(States.Due, ignoreSuppression: true));

            _workflow.SetState(States.Due);
            Assert.Equal(true, _workflow.WasIn(States.Due));
            Assert.Equal(1, _workflow.TimesIn(States.Due));

            _workflow.SetState(States.Contacted);
            Assert.Equal(false, _workflow.WasIn(States.Due));
            Assert.Equal(0, _workflow.TimesIn(States.Due));
            Assert.Equal(true, _workflow.WasIn(States.Due, ignoreSuppression: true));
            Assert.Equal(2, _workflow.TimesIn(States.Due, ignoreSuppression: true));
        }

        [Fact]
        public void TimesInWasInShouldWorkStatesThatWereNotEntered()
        {
            Assert.Equal(false, _workflow.WasIn(States.Outstanding));
            Assert.Equal(0, _workflow.TimesIn(States.Outstanding));
        }

        [Fact]
        public void StateTransitionsDuringStateRestoringShouldNotBeCountedInStateStats()
        {
            _workflow.SetData("StatesHistory", new List<States> { States.Outstanding, States.Due });
            var stateStats = new Dictionary<States, StateStats>
            {
                [States.Outstanding] = new StateStats(2),
                [States.Due] = new StateStats { EnteredCounter = 1, IgnoreSuppressionEnteredCounter = 2 }
            };
            _workflow.SetData("StatesStats", stateStats);
            _workflow.OnLoaded();

            _workflow.SetState(States.Outstanding);

            Assert.Equal(2, _workflow.TimesIn(States.Outstanding));
            Assert.Equal(2, _workflow.TimesIn(States.Outstanding, ignoreSuppression: true));
            Assert.Equal(1, _workflow.TimesIn(States.Due));
            Assert.Equal(2, _workflow.TimesIn(States.Due, ignoreSuppression: true));

            _workflow.SetState(States.Outstanding);
            Assert.Equal(3, _workflow.TimesIn(States.Outstanding));
            Assert.Equal(3, _workflow.TimesIn(States.Outstanding, ignoreSuppression: true));

            _workflow.SetState(States.Due);
            Assert.Equal(1, _workflow.TimesIn(States.Due));
            Assert.Equal(3, _workflow.TimesIn(States.Due, ignoreSuppression: true));
        }

        [Fact]
        public void SetStateOnLoadShouldCallHandlersWithIsRestoringAsTrue()
        {
            _workflow.SetData("StatesHistory", new List<States> { States.Outstanding, States.Due });

            Assert.False(_workflow.IsRestoringState);
            _workflow.OnLoaded();
            Assert.True(_workflow.IsRestoringState);
            Assert.False(_workflow.StatesHistory.Any());

            _workflow.SetState(States.Outstanding);
            Assert.False(_workflow.OnDueStateIsRestoringState);
            _workflow.SetState(States.Due);
            Assert.Equal(0, _workflow.OnOutstandingStateCounter);
            Assert.Equal(1, _workflow.OnDueStateCounter);
            Assert.True(_workflow.OnDueStateIsRestoringState);
        }

        [Fact]
        public void SetStateOnLoadShouldNotSaveWorkflowDataAfterExpectedStateEncountered()
        {
            _workflow.SetData("StatesHistory", new List<States> { States.Outstanding, States.Due });
            _workflow.OnLoaded();
            _workflow.SetState(States.Outstanding);
            Assert.Equal(0, _workflowRepo.SaveWorkflowDataCounter);
        }

        [Fact]
        public void SetStateOnLoadShouldSaveWorkflowDataAndStopRestoringIfUnexpectedStateEncountered()
        {
            _workflow.SetData("StatesHistory", new List<States> { States.Outstanding, States.Due });
            _workflow.OnLoaded();
            Assert.NotEqual(TaskStatus.RanToCompletion, _workflow.StateInitializedTask.Status);
            _workflow.SetState(States.Outstanding);
            _workflow.SetState(States.Contacted);
            Assert.False(_workflow.IsRestoringState);
            Assert.Equal(TaskStatus.RanToCompletion, _workflow.StateInitializedTask.Status);
            Assert.Equal(1, _workflowRepo.SaveWorkflowDataCounter);
            Assert.True(
                new List<States> { States.Outstanding, States.Contacted }.SequenceEqual(_workflow.StatesHistory));
        }

        [Fact]
        public void SetStateOnLoadShouldStopRestoringWhenAllStatesAreRestored()
        {
            _workflow.SetData("StatesHistory", new List<States> { States.Outstanding, States.Due });
            _workflow.OnLoaded();
            _workflow.SetState(States.Outstanding);
            Assert.NotEqual(TaskStatus.RanToCompletion, _workflow.StateInitializedTask.Status);
            _workflow.SetState(States.Due);
            Assert.False(_workflow.IsRestoringState);
            Assert.Equal(TaskStatus.RanToCompletion, _workflow.StateInitializedTask.Status);
            Assert.Equal(1, _workflowRepo.SaveWorkflowDataCounter);
            Assert.True(
                new List<States> { States.Outstanding, States.Due }.SequenceEqual(_workflow.StatesHistory));
            _workflow.SetState(States.Contacted);
            Assert.Equal(2, _workflowRepo.SaveWorkflowDataCounter);
        }

        [Fact]
        public void ForNonRestoredWorkflowsInitializedTaskShouldCompleteOnFirstStateTransition()
        {
            Assert.NotEqual(TaskStatus.RanToCompletion, _workflow.StateInitializedTask.Status);
            _workflow.SetState(States.Outstanding);
            Assert.Equal(TaskStatus.RanToCompletion, _workflow.StateInitializedTask.Status);
        }

        [Fact]
        public async Task GetAvailableActionsShouldReturnOnlyActionsConfiguredForCurrentState()
        {
            _workflow = new TestWorkflow(() => _workflowRepo, doInit: false);
            _workflow.StartWorkflow();
            await _workflow.StartedTask;
            _workflow.SetState(States.Outstanding);
            var actions = await _workflow.GetAvailableActionsAsync();
            Assert.True(actions.SequenceEqual(new[] { "Outstanding Action 1", "Outstanding Action 2" }));
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task ActionCouldBeExecutedViaSynonym()
        {
            _workflow = new TestWorkflow(() => _workflowRepo, doInit: false);
            _workflow.StartWorkflow();
            await _workflow.StartedTask;
            _workflow.SetState(States.Outstanding);
            await _workflow.ExecuteActionAsync("Outstanding 1");
            await _workflow.CompletedTask;
        }

        [Fact]
        public void IfStateCategoryIsConfiguredSecondTimeIoeShouldBeThrown()
        {
            var ex = Record.Exception(() => _workflow.ConfigureStateCategory("Due"));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void IfForStateUnconfiguredStateCategoryIsSpecifiedThenAoorShouldBeThrown()
        {
            var ex = Record.Exception(
                () => _workflow.ConfigureState(
                    States.None,
                    () => { },
                    stateCategories: new[] { "Due 2" }));

            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        [Fact]
        public void IfForStateUnconfiguredActionIsSpecifiedThenAoorShouldBeThrown()
        {
            var ex = Record.Exception(
                () => _workflow.ConfigureState(
                    States.None,
                    () => { },
                    availableActions: new[] { "Bad action" }));

            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        [Fact]
        public async Task GetAvailableActionsShouldReturnActionsConfiguredForStateCategoriesOfCurrentState()
        {
            _workflow = new TestWorkflow(() => _workflowRepo, doInit: false);
            _workflow.StartWorkflow();
            await _workflow.StartedTask;
            _workflow.SetState(States.Due);
            var actions = await _workflow.GetAvailableActionsAsync();
            Assert.True(actions.SequenceEqual(new[] { "Outstanding Action 1", "Due Action 1" }));
            await _workflow.CompletedTask;
        }

        [Fact]
        public async Task GetAvailableActionsShouldIgnoreActionsDisallowedForCurrentState()
        {
            _workflow = new TestWorkflow(() => _workflowRepo, doInit: false);
            _workflow.StartWorkflow();
            await _workflow.StartedTask;
            _workflow.SetState(States.Contacted);
            var actions = await _workflow.GetAvailableActionsAsync();
            Assert.True(actions.SequenceEqual(new[] { "Due Action 1" }));
            await _workflow.CompletedTask;
        }

        private sealed class TestWorkflow : WorkflowBase<States>
        {
            public TestWorkflow(Func<IWorkflowStateRepository> workflowRepoFactory, bool doInit = true) 
                : base(workflowRepoFactory, 4)
            {
                if (doInit)
                {
                    OnInit();
                }
            }

            public new States State => base.State;

            public IList<States> StatesHistory => GetData<IList<States>>(nameof(StatesHistory));

            public IList<Tuple<States, DateTime>> FullStatesHistory => 
                GetData<IList<Tuple<States, DateTime>>>(nameof(FullStatesHistory));

            public bool IsRestoringState => GetTransientData<bool>(nameof(IsRestoringState));

            public int OnOutstandingStateCounter { get; private set; }

            public int OnDueStateCounter { get; private set; }

            public bool OnDueStateIsRestoringState { get; private set; }

            public States NewState { get; set; }

            public States OldState { get; set; }

            // ReSharper disable once UnusedParameter.Local
            public new void SetState(States state) => base.SetState(state);

            public new void OnLoaded() => base.OnLoaded();

            // ReSharper disable UnusedParameter.Local
            public new bool WasIn(States state, bool ignoreSuppression = false) => base.WasIn(state, ignoreSuppression);
            /* ReSharper restore UnusedParameter.Local */

            // ReSharper disable UnusedParameter.Local
            public new int TimesIn(States state, bool ignoreSuppression = false) => base.TimesIn(state, ignoreSuppression);
            /* ReSharper restore UnusedParameter.Local */

            [SuppressMessage("ReSharper", "UnusedParameter.Local", Justification = "It is OK")]
            public new void SetData<T>(string key, T value) => base.SetData(key, value);

            [SuppressMessage("ReSharper", "UnusedParameter.Local", Justification = "It is OK")]
            public new void ConfigureStateCategory(
                string categoryName = null,
                IEnumerable<string> availableActions = null) =>
                    base.ConfigureStateCategory(categoryName, availableActions);

            [SuppressMessage("ReSharper", "UnusedParameter.Local", Justification = "It is OK")]
            public void ConfigureState(
                States state,
                Action onStateHandler,
                IEnumerable<string> availableActions = null,
                IEnumerable<string> stateCategories = null)
            {
                base.ConfigureState(
                    state,
                    onStateHandler,
                    availableActions: availableActions,
                    stateCategories: stateCategories);
            }

            protected override async Task RunAsync()
            {
                await Task.Delay(30);
            }

            protected override void OnInit()
            {
                base.OnInit();

                StateChanged += OnStateChanged;
            }

            protected override void OnActionsInit()
            {
                base.OnActionsInit();
                ConfigureAction("Outstanding Action 1", synonyms: new[] { "Outstanding 1" });
                ConfigureAction("Outstanding Action 2");
                ConfigureAction("Due Action 1");
                ConfigureAction("Contacted Action 1");
            }

            protected override void OnStatesInit()
            {
                ConfigureStateCategory(availableActions: new[] { "Outstanding Action 1" });
                ConfigureStateCategory("Due", new[] { "Due Action 1" });

                ConfigureState(
                    States.Outstanding,
                    OnOutstandingState,
                    suppressAll: true,
                    availableActions: new[] { "Outstanding Action 2", "Outstanding Action 1" });
                ConfigureState(
                    States.Due,
                    OnDueState,
                    suppressStates: new[] { States.Contacted },
                    stateCategories: new[] { "Due" });
                ConfigureState(
                    States.Contacted,
                    OnContactedState,
                    suppressStates: new[] { States.Due },
                    disallowedActions: new[] { "Outstanding Action 1" },
                    stateCategories: new[] { "Due" });
            }

            private void OnStateChanged(object sender, StateChangedEventArgs args)
            {
                OldState = args.OldState;
                NewState = args.NewState;
            }

            private void OnOutstandingState()
            {
                ++OnOutstandingStateCounter;
            }

            private void OnDueState(bool isRestoringState)
            {
                ++OnDueStateCounter;
                OnDueStateIsRestoringState = isRestoringState;
            }

            private void OnContactedState()
            {
            }
        }

        private class WorkflowRepository : IWorkflowStateRepository
        {
            public int SaveWorkflowDataCounter { get; private set; }

            public void SaveWorkflowData(WorkflowBase workflow)
            {
                Assert.NotNull(workflow);
                ++SaveWorkflowDataCounter;
            }

            public void MarkWorkflowAsCompleted(WorkflowBase workflow)
            {
            }

            public void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception)
            {
                throw new NotImplementedException();
            }

            public void MarkWorkflowAsCanceled(WorkflowBase workflow)
            {
                throw new NotImplementedException();
            }

            public void MarkWorkflowAsSleeping(WorkflowBase workflow)
            {
                throw new NotImplementedException();
            }

            public void MarkWorkflowAsInProgress(WorkflowBase workflow)
            {
                throw new NotImplementedException();
            }
        }
    }
}
