using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowsCore.MonteCarlo;
using WorkflowsCore.Time;

namespace WorkflowsCore.Tests
{
    [TestClass]
    public class MonteCarloSimulatorTests
    {
        private MonteCarloSimulator<TestWorkflow> _simulator;
        private TestWorkflow _lastWorkflow;

        private enum States
        {
            // ReSharper disable once UnusedMember.Local
            None = 0,
            Due = 1,
            Contacted = 2
        }

        [TestInitialize]
        public void TestInitialize()
        {
            _simulator =
                new MonteCarloSimulator<TestWorkflow>(
                    () => _lastWorkflow = new TestWorkflow(() => new WorkflowRepository()));
        }

        [TestMethod]
        public void SimulationShouldSelectRandomEventBasedOnConfiguredEvents()
        {
            var numberOfClockEvents = 0;
            _simulator.ConfigureWorldClockAdvancingEvent(
                1.0,
                (w, _) =>
                {
                    Interlocked.Increment(ref numberOfClockEvents);
                    return Task.FromResult(new WorldClockAdvancingEvent(Globals.TimeProvider.Now.AddHours(1)));
                });

            var numberOfCustomEvents = 0;
            _simulator.ConfigureCustomEvent(
                2.0,
                (w, concurrentEvents) =>
                {
                    Assert.IsFalse(concurrentEvents);
                    Interlocked.Increment(ref numberOfCustomEvents);
                    return Task.CompletedTask;
                });

            _simulator.RunSimulations(1, 1000, randomGeneratorSeed: 171787817);
            Console.WriteLine($"numberOfClockEvents: {numberOfClockEvents}, numberOfCustomEvents: {numberOfCustomEvents}");
            Assert.AreEqual(1000, numberOfClockEvents + numberOfCustomEvents);
            Assert.AreEqual(361, numberOfClockEvents);
            Assert.AreEqual(639, numberOfCustomEvents);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void IfNoEventsIsConfiguredThenRunSimulationsShouldThrowException()
        {
            _simulator.RunSimulations(1, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void EventConfigurationShouldNotAllowZeroProbabilityWeight()
        {
            _simulator.ConfigureWorldClockAdvancingEvent(
                1.0,
                (w, _) => Task.FromResult(new WorldClockAdvancingEvent(Globals.TimeProvider.Now.AddHours(1))));
            _simulator.ConfigureApplicationRestartEvent(0.0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void EventConfigurationShouldNotAllowNegativeProbabilityWeight()
        {
            _simulator.ConfigureActionExecutionEvent(-0.5);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ApplicationRestartEventCannotBeConfiguredTwice()
        {
            _simulator.ConfigureApplicationRestartEvent(1.0);
            _simulator.ConfigureApplicationRestartEvent(2.0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ApplicationRestartEventCannotBeConfiguredIfWorldClockAdvancingEventIsConfigured()
        {
            _simulator.ConfigureApplicationRestartEvent(1.0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ActionExecutionEventCannotBeConfiguredTwice()
        {
            _simulator.ConfigureActionExecutionEvent(1.0);
            _simulator.ConfigureActionExecutionEvent(2.0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SpecificActionExecutionEventCannotBeConfiguredIfActionExecutionEventWasNotConfigured()
        {
            _simulator.ConfigureActionExecutionEvent(1.0, "Action 1", w => Task.CompletedTask);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SpecificActionExecutionEventCannotBeConfiguredTwice()
        {
            _simulator.ConfigureActionExecutionEvent(1.0);
            _simulator.ConfigureActionExecutionEvent(1.0, "Action 1", w => Task.CompletedTask);
            _simulator.ConfigureActionExecutionEvent(1.0, "Action 1", w => Task.CompletedTask);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void IfActionExecutionEventIsConfiguredThenAtLeastOneSpecificActionShouldBeConfigured()
        {
            _simulator.ConfigureActionExecutionEvent(1.0);
            _simulator.RunSimulations(1, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void WorldClockAdvancingEventCannotBeConfiguredTwice()
        {
            _simulator.ConfigureWorldClockAdvancingEvent(
                1.0,
                (w, _) => Task.FromResult(new WorldClockAdvancingEvent(DateTime.MaxValue)));
            _simulator.ConfigureWorldClockAdvancingEvent(
                2.0,
                (w, _) => Task.FromResult(new WorldClockAdvancingEvent(DateTime.MaxValue)));
        }

        [TestMethod]
        public void SimulationShouldStopIfWorkflowIsCompleted()
        {
            var numberOfCustomEvents = 0;
            _simulator.ConfigureCustomEvent(
                1.0,
                w =>
                {
                    ++numberOfCustomEvents;
                    return Task.Delay(1);
                });
            _simulator.RunSimulations(
                1,
                1000,
                getInitialWorkflowData: () => new Dictionary<string, object> { ["CompleteFast"] = true });

            Assert.IsTrue(numberOfCustomEvents < 1000);
        }

        [TestMethod]
        public void SimulationShouldStopWorkflowIfMaxNumberOfEventsAreReached()
        {
            _simulator.ConfigureCustomEvent(1.0, w => Task.CompletedTask);
            _simulator.RunSimulations(1, 1);
            Assert.IsTrue(_lastWorkflow.CompletedTask.IsFaulted);
            Assert.AreEqual(
                "Workflow was stopped because maximum number of events were reached",
                _lastWorkflow.CompletedTask.Exception?.GetBaseException().Message);
        }

        [TestMethod]
        public void WorldClockAdvancingEventShouldUpdateWorldClockAndThenWaitForSpecifiedTask()
        {
            Task t = null;
            _simulator.ConfigureWorldClockAdvancingEvent(
                1.0,
                (w, isAppRestart) =>
                {
                    Assert.AreEqual(false, isAppRestart);
                    return Task.FromResult(
                        new WorldClockAdvancingEvent(
                            Globals.TimeProvider.Now.AddHours(1).AddMinutes(23),
                            t = Task.Delay(10)));
                });
            var begin = default(DateTime);
            _simulator.RunSimulations(
                1,
                1,
                beforeSimulationCallback: () => begin = Globals.TimeProvider.Now,
                afterSimulationCallback: () =>
                {
                    Assert.AreEqual(TaskStatus.RanToCompletion, t.Status);
                    var newTime = begin.AddHours(1).AddMinutes(23);
                    Assert.AreEqual(newTime, Globals.TimeProvider.Now);
                    var events = Globals.EventMonitor.GetEvents();
                    Assert.AreEqual(3, events.Count);
                    Assert.AreEqual("World clock is advanced", events[0].Name);
                    Assert.AreEqual(newTime.ToString("yyyy-MM-dd HH:mm:ss"), events[0].Parameters);
                });
        }

        [TestMethod]
        public void CustomEventShouldNotBeExecutedIfNotAvailable()
        {
            _simulator.ConfigureWorldClockAdvancingEvent(
                1.0,
                (w, _) => Task.FromResult(new WorldClockAdvancingEvent(Globals.TimeProvider.Now.AddHours(1))));
            _simulator.ConfigureCustomEvent(
                100.0,
                w => Task.FromException(new Exception()),
                w => Task.FromResult(false));
            _simulator.RunSimulations(1, 10);
        }

        [TestMethod]
        public void CustomEventShouldExecuteCustomEventHandler()
        {
            var counter = 0;
            _simulator.ConfigureCustomEvent(
                1.0,
                w =>
                {
                    ++counter;
                    return Task.CompletedTask;
                });
            _simulator.RunSimulations(
                1,
                1,
                afterSimulationCallback: () =>
                {
                    var events = Globals.EventMonitor.GetEvents();
                    Assert.AreEqual(2, events.Count);
                    Assert.AreEqual(
                        "Workflow was stopped because maximum number of events were reached",
                        events[0].Name);
                    Assert.AreEqual("Last workflow state is Due", events[1].Name);
                });
            Assert.AreEqual(1, counter);
        }

        [TestMethod]
        public void ActionExectionEventShouldNotBeExecutedIfNoActionsAvailable()
        {
            _simulator.ConfigureActionExecutionEvent(100.0);
            _simulator.ConfigureCustomEvent(0.1, w => Task.CompletedTask);
            var action1Counter = 0;
            _simulator.ConfigureActionExecutionEvent(
                1.0,
                "Action 1",
                w =>
                {
                    ++action1Counter;
                    return Task.CompletedTask;
                });

            _simulator.RunSimulations(
                1,
                1,
                getInitialWorkflowData: () => new Dictionary<string, object> { ["NoActions"] = true });
            Assert.AreEqual(0, action1Counter);
        }

        [TestMethod]
        public void ActionExectionEventShouldSelectRandomlySpecificActionAndExecuteItsHandler()
        {
            _simulator.ConfigureActionExecutionEvent(1.0);
            var action1Counter = 0;
            _simulator.ConfigureActionExecutionEvent(
                3.0,
                "Action 1",
                w =>
                {
                    ++action1Counter;
                    return Task.CompletedTask;
                });

            _simulator.ConfigureActionExecutionEvent(100.0, "Action 2", w => Task.FromException(new Exception()));

            var action3Counter = 0;
            _simulator.ConfigureActionExecutionEvent(
                1.0,
                "Action 3",
                w =>
                {
                    ++action3Counter;
                    return Task.Delay(1);
                });

            _simulator.RunSimulations(
                1,
                5,
                randomGeneratorSeed: 171787817,
                afterSimulationCallback: () =>
                {
                    var events = Globals.EventMonitor.GetEvents();
                    Assert.AreEqual(2, events.Count);
                    Assert.AreEqual(
                        "Workflow was stopped because maximum number of events were reached",
                        events[0].Name);
                });
            Assert.AreEqual(4, action1Counter);
            Assert.AreEqual(1, action3Counter);
        }

        [TestMethod]
        public void ApplicationRestartEventShouldStopWorkflowAdvanceWorldClockAndRerunWorkflow()
        {
            var clockAdvanced = false;
            WorkflowBase oldWorkflow = null;
            _simulator.ConfigureWorldClockAdvancingEvent(
                0.1,
                (w, isAppRestart) =>
                {
                    clockAdvanced = true;
                    oldWorkflow = w;
                    Assert.IsTrue(w.CompletedTask.IsCompleted);
                    Assert.AreEqual(true, isAppRestart);
                    Assert.AreEqual(1, ((IWorkflowData)w).Data["Id"]);
                    return Task.FromResult(
                        new WorldClockAdvancingEvent(Globals.TimeProvider.Now.AddHours(1).AddMinutes(23)));
                });
            var customEventCalled = false;
            _simulator.ConfigureCustomEvent(
                100.0,
                w =>
                {
                    customEventCalled = true;
                    Assert.IsNotNull(oldWorkflow);
                    Assert.AreNotSame(oldWorkflow, w);
                    Assert.AreEqual(1, w.GetDataAsync<int>("Id").Result);
                    return Task.CompletedTask;
                });
            _simulator.ConfigureApplicationRestartEvent(100.0);

            var counter = 0;
            _simulator.RunSimulations(
                1,
                6,
                getInitialWorkflowData: () => new Dictionary<string, object> { ["Id"] = ++counter },
                randomGeneratorSeed: 171787817,
                afterSimulationCallback: () =>
                {
                    var events = Globals.EventMonitor.GetEvents();
                    Assert.AreEqual(22, events.Count);
                    Assert.AreEqual("Application is shutdown", events[0].Name);
                    Assert.AreEqual("Workflow was stopped due to application shutdown", events[1].Name);
                    Assert.AreEqual("World clock is advanced", events[2].Name);
                    Assert.AreEqual("Application is started", events[3].Name);
                });
            Assert.IsTrue(clockAdvanced);
            Assert.IsTrue(customEventCalled);
        }

        [TestMethod]
        public void IfWorkflowIsFailedThenSimulationShouldStopAndItsSequenceOfEventsShouldBeReported()
        {
            _simulator.ConfigureActionExecutionEvent(1.0);
            _simulator.ConfigureActionExecutionEvent(
                1.0,
                "Action 4",
                w =>
                {
                    Globals.EventMonitor.LogEvent("Action 4", "some parameters");
                    return w.ExecuteActionAsync("Action 4");
                });
            _simulator.ConfigureCustomEvent(
                1.0,
                w =>
                {
                    Globals.EventMonitor.LogEvent("Custom event");
                    return Task.CompletedTask;
                });
            try
            {
                var stats = _simulator.RunSimulations(1, 6, randomGeneratorSeed: 171787817);
                Console.WriteLine(stats);
                Assert.Fail();
            }
            catch (SimulationException ex)
            {
                Console.WriteLine(ex);
                Assert.AreEqual(6, ex.Events.Count);
                Assert.AreEqual("Custom event", ex.Events[0].Name);
                Assert.AreEqual("Action 4", ex.Events[5].Name);
            }
        }

        [TestMethod]
        public void IfEventIsFailedThenSimulationShouldStopAndItsSequenceOfEventsShouldBeReported()
        {
            _simulator.ConfigureCustomEvent(
                1.0,
                w =>
                {
                    Globals.EventMonitor.LogEvent("Custom event");
                    throw new NullReferenceException();
                });
            try
            {
                _simulator.RunSimulations(1, 2, randomGeneratorSeed: 171787817);
                Assert.Fail();
            }
            catch (SimulationException ex)
            {
                Console.WriteLine(ex);
                Assert.AreEqual(1, ex.Events.Count);
                Assert.AreEqual("Custom event", ex.Events[0].Name);
            }
        }

        [TestMethod]
        public void IfSimulationFailsThenOtherSimulationsShouldBeTerminatedOrNotRun()
        {
            _simulator.ConfigureActionExecutionEvent(1.0);
            var action4Counter = 0;
            _simulator.ConfigureActionExecutionEvent(
                1.0,
                "Action 4",
                w =>
                {
                    Globals.EventMonitor.LogEvent("Action 4", "some parameters");
                    return ++action4Counter > 1 ? Task.Delay(1) : w.ExecuteActionAsync("Action 4");
                });
            _simulator.ConfigureCustomEvent(
                1.0,
                w =>
                {
                    Globals.EventMonitor.LogEvent("Custom event");
                    return Task.Delay(1);
                });
            try
            {
                _simulator.RunSimulations(1000, 1000, randomGeneratorSeed: 171787817, maxConcurrentSimulations: 1);
                Assert.Fail();
            }
            catch (SimulationException ex)
            {
                Console.WriteLine(ex);
                Assert.IsTrue(ex.Events.Count >= 6 && ex.Events.Count <= 7);
            }
        }

        [TestMethod]
        public void IfAllSimulationsAreCompletedSuccessfullyThenSimulationsStatsShouldBeReturned()
        {
            var action1Counter = 0;
            _simulator.ConfigureCustomEvent(
                1.0,
                w =>
                {
                    Globals.EventMonitor.LogEvent("Custom event 1");
                    ++action1Counter;
                    return Task.CompletedTask;
                });
            var action2Counter = 0;
            _simulator.ConfigureCustomEvent(
                2.0,
                w =>
                {
                    Globals.EventMonitor.LogEvent("Custom event 2");
                    ++action2Counter;
                    return Task.CompletedTask;
                });

            var stats = _simulator.RunSimulations(5, 5, randomGeneratorSeed: 171787817, maxConcurrentSimulations: 1);
            Assert.IsNotNull(stats);
            Console.WriteLine(stats);
            Assert.AreEqual(5, stats.NumberOfSimulations);
            Assert.AreEqual(35, stats.TotalNumberOfEvents);
            Assert.AreEqual(7.0, stats.AverageNumberOfEventsPerSimulation);
            Assert.AreEqual(action2Counter, stats.Events[0].Item2);
            Assert.AreEqual(action1Counter, stats.Events[1].Item2);
        }

        [TestMethod]
        [ExpectedException(typeof(SimulationException))]
        public void SimulationShouldFailIfMaxEventProcessingTimeIsExceeded()
        {
            _simulator.ConfigureCustomEvent(1.0, w => Task.Delay(100));            
            _simulator.RunSimulations(1, 5, maxEventProcessingTime: 10);
        }

        [TestMethod]
        public void IfMaxConcurrentEventsIsGreaterThen1ThenEventsShouldBeRunConcurrently()
        {
            _simulator =
                new MonteCarloSimulator<TestWorkflow>(
                    () => _lastWorkflow = new TestWorkflow(() => new WorkflowRepository()),
                    maxConcurrentEvents: 5);

            var action1Counter = 0;
            var action1Counter2 = 0;
            _simulator.ConfigureCustomEvent(
                1.0,
                (w, concurrentEvents) =>
                {
                    Assert.IsTrue(concurrentEvents);
                    ++action1Counter2;
                    Globals.EventMonitor.LogEvent("Custom event 1");
                    Interlocked.Increment(ref action1Counter);
                    return Task.CompletedTask;
                });
            var action2Counter = 0;
            var action2Counter2 = 0;
            _simulator.ConfigureCustomEvent(
                2.0,
                w =>
                {
                    Globals.EventMonitor.LogEvent("Custom event 2");
                    ++action2Counter;
                    Interlocked.Increment(ref action2Counter2);
                    return Task.CompletedTask;
                },
                isSequential: true);

            var stats = _simulator.RunSimulations(
                5,
                5000,
                randomGeneratorSeed: 171787817,
                getEventsAvailabilityAwaiter: w => new WorkflowActionsAvailabilityAwaiter<States>(w));
            Assert.IsNotNull(stats);
            Console.WriteLine(stats);
            Assert.AreEqual(action2Counter, action2Counter);
            Assert.AreNotEqual(action1Counter, action1Counter2);
            Assert.IsTrue(action1Counter > action2Counter);
        }

        [TestMethod]
        public async Task WorkflowActionsAvailabilityAwaiterShouldWaitForWorkflowStateChange()
        {
            Utilities.TimeProvider = new TestingTimeProvider();
            var workflow = new TestWorkflow(() => new WorkflowRepository());
            workflow.StartWorkflow(initialWorkflowData: new Dictionary<string, object> { ["NoActions"] = true });
            await workflow.StateInitializedTask;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Delay(10)
                .ContinueWith(_ => TestingTimeProvider.Current.SetCurrentTime(Globals.TimeProvider.Now.AddHours(17)));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            await new WorkflowActionsAvailabilityAwaiter<States>(workflow).Task;

            var state = await workflow.GetStateAsync();
            Assert.AreEqual(States.Due, state);
        }

        [TestMethod]
        public async Task WorkflowActionsAvailabilityAwaiterShouldCompleteIfWorkflowIsStoppedOrCompleted()
        {
            Utilities.TimeProvider = new TestingTimeProvider();
            var workflow = new TestWorkflow(() => new WorkflowRepository());
            workflow.StartWorkflow(
                initialWorkflowData: new Dictionary<string, object> { ["NoActions"] = true, ["CompleteFast"] = true });
            await workflow.StateInitializedTask;

            var state = await workflow.GetStateAsync();
            Assert.AreEqual(States.Contacted, state);
            await new WorkflowActionsAvailabilityAwaiter<States>(workflow).Task;

            Assert.AreEqual(TaskStatus.RanToCompletion, workflow.CompletedTask.Status);
        }

        [TestMethod]
        public void EventsAvailabilityAwaiterShouldBeAwaitedBeforeGeneratingEventsInNonPrimaryPartitions()
        {
            _simulator =
                new MonteCarloSimulator<TestWorkflow>(
                    () => _lastWorkflow = new TestWorkflow(() => new WorkflowRepository()),
                    maxConcurrentEvents: 2);

            _simulator.ConfigureWorldClockAdvancingEvent(
                0.1,
                (w, isAppRestart) =>
                    Task.FromResult(new WorldClockAdvancingEvent(Globals.TimeProvider.Now.AddHours(1))));

            _simulator.ConfigureActionExecutionEvent(1.0);
            _simulator.ConfigureActionExecutionEvent(
                1.0,
                "Action 1",
                async w =>
                {
                    Globals.EventMonitor.LogEvent("Action 1");
                    await Task.Delay(1);
                    await w.ExecuteActionAsync("Action 1");
                });

            var stats = _simulator.RunSimulations(
                1,
                100,
                randomGeneratorSeed: 171787817,
                getInitialWorkflowData: () => new Dictionary<string, object> { ["NoActions"] = true },
                getEventsAvailabilityAwaiter: w => new WorkflowActionsAvailabilityAwaiter<States>(w));
            Assert.IsNotNull(stats);
            Console.WriteLine(stats);
        }

        private class TestWorkflow : WorkflowBase<States>
        {
            public TestWorkflow(Func<IWorkflowStateRepository> workflowRepoFactory)
                : base(workflowRepoFactory)
            {
            }

            protected override void OnActionsInit()
            {
                base.OnActionsInit();
                ConfigureAction("Action 1");
                ConfigureAction("Action 2");
                ConfigureAction("Action 3");
                ConfigureAction("Action 4");
            }

            protected override void OnStatesInit()
            {
                ConfigureState(States.Due, availableActions: new[] { "Action 1", "Action 3", "Action 4" });
                ConfigureState(States.Contacted);
            }

            protected override async Task RunAsync()
            {
                SetState(!GetData<bool>("NoActions") ? States.Due : States.Contacted);
                await this.WaitForAny(
                    () => Task.Delay(GetData<bool>("CompleteFast") ? 1 : 10000, Utilities.CurrentCancellationToken),
                    async () =>
                    {
                        await this.WaitForAction("Action 4");
                        throw new NullReferenceException();
                    },
                    () => this.Optional(WaitForDueDate()));
            }

            private async Task WaitForDueDate()
            {
                await this.WaitForDate(Globals.TimeProvider.Now.AddHours(17));
                SetState(States.Due);
            }
        }

        private class WorkflowRepository : IWorkflowStateRepository
        {
            public void SaveWorkflowData(WorkflowBase workflow)
            {
            }

            public void MarkWorkflowAsCompleted(WorkflowBase workflow)
            {
            }

            public void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception)
            {
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
