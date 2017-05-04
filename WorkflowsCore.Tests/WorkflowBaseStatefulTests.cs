using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowBaseStatefulTests
    {
        public enum States
        {
            // ReSharper disable once UnusedMember.Local
            None,
            Outstanding,
            Due,
            Contacted
        }

        public class GeneralTests
        {
            private readonly TestWorkflow _workflow = new TestWorkflow();

            [Fact]
            public void SetStateShouldUpdateState()
            {
                Assert.NotEqual(States.Outstanding, _workflow.State);
                _workflow.SetState(States.Outstanding);
                Assert.Equal(States.Outstanding, _workflow.State);
                Assert.Equal(States.None, _workflow.OldState);
                Assert.Equal(States.Outstanding, _workflow.NewState);

                _workflow.SetState(States.Due);
                Assert.Equal(States.Outstanding, _workflow.PreviousState);
                Assert.Equal(States.Due, _workflow.State);
            }

            [Fact]
            public void SetStateShouldUpdateStatesHistory()
            {
                _workflow.SetState(States.Outstanding);
                Assert.Equal(new[] { States.Outstanding }, _workflow.StatesHistory.ToArray());
            }

            [Fact]
            public void SetStateShouldKeepOnlyLastTwoStates()
            {
                _workflow.SetState(States.Outstanding);
                _workflow.SetState(States.Due);
                _workflow.SetState(States.Outstanding);
                Assert.Equal(new[] { States.Due, States.Outstanding }, _workflow.StatesHistory.ToArray());
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
            public void TimesInWasInShouldWorkForStatesThatWereNotEntered()
            {
                Assert.Equal(false, _workflow.WasIn(States.Outstanding));
                Assert.Equal(0, _workflow.TimesIn(States.Outstanding));
            }

            [Fact]
            public void StateTransitionsDuringStateRestoringShouldNotBeCountedInStateStats()
            {
                _workflow.SetData("StatesHistory", new List<States> { States.Outstanding, States.Due });
                var stateStats = new Dictionary<States, int>
                {
                    [States.Outstanding] = 2,
                    [States.Due] = 1
                };
                _workflow.SetData("StatesStats", stateStats);
                _workflow.OnLoaded();

                _workflow.SetState(States.Outstanding, isStateRestored: true);

                Assert.Equal(2, _workflow.TimesIn(States.Outstanding));
                Assert.Equal(1, _workflow.TimesIn(States.Due));

                _workflow.SetState(States.Outstanding);
                Assert.Equal(3, _workflow.TimesIn(States.Outstanding));

                _workflow.SetState(States.Due);
                Assert.Equal(2, _workflow.TimesIn(States.Due));
            }

            [Fact]
            public void ForRestoredWorkflowsWithoutHistoryIsLoadedShouldBeFalse()
            {
                _workflow.OnLoaded();
                Assert.False(_workflow.IsLoaded);
            }
        }

        public class TestingTimeProviderTests
        {
            private readonly TestWorkflow _workflow = new TestWorkflow();

            public TestingTimeProviderTests()
            {
                Utilities.TimeProvider = new TestingTimeProvider();
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
        }

        public class WorkflowDataTests
        {
            private readonly WorkflowRepository _workflowRepo;
            private readonly TestWorkflow _workflow;

            public WorkflowDataTests()
            {
                _workflowRepo = new WorkflowRepository();
                _workflow = new TestWorkflow(() => _workflowRepo);
            }

            [Fact]
            public void SetStateShouldSaveWorkflowData()
            {
                _workflow.SetState(States.Outstanding);
                Assert.Equal(1, _workflowRepo.SaveWorkflowDataCounter);
            }

            [Fact]
            public void SetStateOnLoadShouldNotSaveWorkflowDataAfterExpectedStateEncountered()
            {
                _workflow.SetData("StatesHistory", new List<States> { States.Outstanding, States.Due });
                _workflow.OnLoaded();
                _workflow.SetState(States.Outstanding, isStateRestored: true);
                Assert.Equal(0, _workflowRepo.SaveWorkflowDataCounter);
            }
        }

        public sealed class TestWorkflow : WorkflowBase<States>
        {
            public TestWorkflow(Func<IWorkflowStateRepository> workflowRepoFactory = null, bool doInit = true) 
                : base(workflowRepoFactory, 4)
            {
                if (doInit)
                {
                    OnInit();
                }
            }

            public new States PreviousState => base.PreviousState;

            public new States State => base.State;

            public IList<States> StatesHistory => 
                GetDataFieldAsync<IList<States>>(nameof(StatesHistory), forceExecution: true).Result;

            public IList<Tuple<States, DateTime, bool>> FullStatesHistory
            {
                get
                {
                    return GetDataFieldAsync<IList<Tuple<States, DateTime, bool>>>(
                        nameof(FullStatesHistory),
                        forceExecution: true).Result;
                }
            }

            public new bool IsLoaded => base.IsLoaded;

            public States NewState { get; set; }

            public States OldState { get; set; }

            // ReSharper disable once UnusedParameter.Local
            public new void SetState(States state, bool isStateRestored = false) => 
                base.SetState(state, isStateRestored);

            public new void OnLoaded() => base.OnLoaded();

            // ReSharper disable UnusedParameter.Local
            public new bool WasIn(States state) => base.WasIn(state);
            /* ReSharper restore UnusedParameter.Local */

            // ReSharper disable UnusedParameter.Local
            public new int TimesIn(States state) => base.TimesIn(state);
            /* ReSharper restore UnusedParameter.Local */

            [SuppressMessage("ReSharper", "UnusedParameter.Local", Justification = "It is OK")]
            public void SetData<T>(string key, T value) =>
                DoWorkflowTaskAsync(w => Metadata.SetDataField(w, key, value), forceExecution: true).Wait();

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);

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
            }

            private void OnStateChanged(object sender, StateChangedEventArgs args)
            {
                OldState = args.OldState;
                NewState = args.NewState;
            }
        }

        private class WorkflowRepository : DummyWorkflowStateRepository
        {
            public int SaveWorkflowDataCounter { get; private set; }

            public override void SaveWorkflowData(WorkflowBase workflow, DateTime? nextActivationDate)
            {
                Assert.NotNull(workflow);
                ++SaveWorkflowDataCounter;
            }
        }
    }
}
