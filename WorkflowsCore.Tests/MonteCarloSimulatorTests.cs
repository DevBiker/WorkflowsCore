﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.MonteCarlo;
using WorkflowsCore.Time;
using Xunit;
using Xunit.Abstractions;

namespace WorkflowsCore.Tests
{
    public class MonteCarloSimulatorTests
    {
        private readonly ITestOutputHelper _output;
        private MonteCarloSimulator<TestWorkflow> _simulator;
        private TestWorkflow _lastWorkflow;

        public MonteCarloSimulatorTests(ITestOutputHelper output)
        {
            _output = output;
            _simulator = new MonteCarloSimulator<TestWorkflow>(() => _lastWorkflow = new TestWorkflow());
        }

        private enum States
        {
            // ReSharper disable once UnusedMember.Local
            None = 0,
            Due = 1,
            Contacted = 2
        }

        [Fact]
        public void SimulationShouldSelectRandomEventBasedOnConfiguredEvents()
        {
            var numberOfClockEvents = 0;
            _simulator.ConfigureSystemClockAdvancingEvent(
                1.0,
                (w, _) =>
                {
                    Interlocked.Increment(ref numberOfClockEvents);
                    return Task.FromResult(new SystemClockAdvancingEvent(Globals.SystemClock.Now.AddHours(1)));
                });

            var numberOfCustomEvents = 0;
            _simulator.ConfigureCustomEvent(
                2.0,
                (w, concurrentEvents) =>
                {
                    Assert.False(concurrentEvents);
                    Interlocked.Increment(ref numberOfCustomEvents);
                    return Task.CompletedTask;
                });

            _simulator.RunSimulations(1, 1000, randomGeneratorSeed: 171787817);
            _output.WriteLine($"numberOfClockEvents: {numberOfClockEvents}, numberOfCustomEvents: {numberOfCustomEvents}");
            Assert.Equal(1000, numberOfClockEvents + numberOfCustomEvents);
            Assert.Equal(361, numberOfClockEvents);
            Assert.Equal(639, numberOfCustomEvents);
        }

        [Fact]
        public void IfNoEventsIsConfiguredThenRunSimulationsShouldThrowException()
        {
            var ex = Record.Exception(() => _simulator.RunSimulations(1, 1));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void EventConfigurationShouldNotAllowZeroProbabilityWeight()
        {
            _simulator.ConfigureSystemClockAdvancingEvent(
                1.0,
                (w, _) => Task.FromResult(new SystemClockAdvancingEvent(Globals.SystemClock.Now.AddHours(1))));
            var ex = Record.Exception(() => _simulator.ConfigureApplicationRestartEvent(0.0));

            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        [Fact]
        public void EventConfigurationShouldNotAllowNegativeProbabilityWeight()
        {
            var ex = Record.Exception(() => _simulator.ConfigureActionExecutionEvent(-0.5));

            Assert.IsType<ArgumentOutOfRangeException>(ex);
        }

        [Fact]
        public void ApplicationRestartEventCannotBeConfiguredTwice()
        {
            _simulator.ConfigureSystemClockAdvancingEvent(
                1.0,
                (w, _) => Task.FromResult(new SystemClockAdvancingEvent(Globals.SystemClock.Now.AddHours(1))));
            _simulator.ConfigureApplicationRestartEvent(1.0);
            var ex = Record.Exception(() => _simulator.ConfigureApplicationRestartEvent(2.0));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void ApplicationRestartEventCannotBeConfiguredIfSystemClockAdvancingEventIsConfigured()
        {
            var ex = Record.Exception(() => _simulator.ConfigureApplicationRestartEvent(1.0));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void ActionExecutionEventCannotBeConfiguredTwice()
        {
            _simulator.ConfigureActionExecutionEvent(1.0);
            var ex = Record.Exception(() => _simulator.ConfigureActionExecutionEvent(2.0));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void SpecificActionExecutionEventCannotBeConfiguredIfActionExecutionEventWasNotConfigured()
        {
            var ex = Record.Exception(
                () => _simulator.ConfigureActionExecutionEvent(1.0, "Action 1", w => Task.CompletedTask));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void SpecificActionExecutionEventCannotBeConfiguredTwice()
        {
            _simulator.ConfigureActionExecutionEvent(1.0);
            _simulator.ConfigureActionExecutionEvent(1.0, "Action 1", w => Task.CompletedTask);
            var ex = Record.Exception(
                () => _simulator.ConfigureActionExecutionEvent(1.0, "Action 1", w => Task.CompletedTask));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void IfActionExecutionEventIsConfiguredThenAtLeastOneSpecificActionShouldBeConfigured()
        {
            _simulator.ConfigureActionExecutionEvent(1.0);
            var ex = Record.Exception(() => _simulator.RunSimulations(1, 1));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
        public void SystemClockAdvancingEventCannotBeConfiguredTwice()
        {
            _simulator.ConfigureSystemClockAdvancingEvent(
                1.0,
                (w, _) => Task.FromResult(new SystemClockAdvancingEvent(DateTime.Now)));
            var ex = Record.Exception(
                () => _simulator.ConfigureSystemClockAdvancingEvent(
                    2.0,
                    (w, _) => Task.FromResult(new SystemClockAdvancingEvent(DateTime.Now))));

            Assert.IsType<InvalidOperationException>(ex);
        }

        [Fact]
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

            Assert.True(numberOfCustomEvents < 1000);
        }

        [Fact]
        public void SimulationShouldStopWorkflowIfMaxNumberOfEventsAreReached()
        {
            _simulator.ConfigureCustomEvent(1.0, w => Task.CompletedTask);
            _simulator.RunSimulations(1, 1);
            Assert.True(_lastWorkflow.CompletedTask.IsFaulted);
            Assert.Equal(
                "Workflow was stopped because maximum number of events were reached",
                _lastWorkflow.CompletedTask.Exception?.GetBaseException().Message);
        }

        [Fact]
        public void SystemClockAdvancingEventShouldUpdateSystemClockAndThenWaitForSpecifiedTask()
        {
            Task t = null;
            _simulator.ConfigureSystemClockAdvancingEvent(
                1.0,
                (w, isAppRestart) =>
                {
                    Assert.False(isAppRestart);
                    return Task.FromResult(
                        new SystemClockAdvancingEvent(
                            Globals.SystemClock.Now.AddHours(1).AddMinutes(23),
                            t = Task.Delay(100)));
                });
            var begin = default(DateTime);
            _simulator.RunSimulations(
                1,
                1,
                beforeSimulationCallback: () => begin = Globals.SystemClock.Now,
                afterSimulationCallback: () =>
                {
                    Assert.Equal(TaskStatus.RanToCompletion, t.Status);
                    var newTime = begin.AddHours(1).AddMinutes(23);
                    Assert.Equal(newTime, Globals.SystemClock.Now);
                    var events = Globals.EventMonitor.GetEvents();
                    Assert.Equal(3, events.Count);
                    Assert.Equal("System clock is advanced", events[0].Name);
                    Assert.Equal(newTime.ToString("yyyy-MM-dd HH:mm:ss"), events[0].Parameters);
                });
        }

        [Fact]
        public void CustomEventShouldNotBeExecutedIfNotAvailable()
        {
            _simulator.ConfigureSystemClockAdvancingEvent(
                1.0,
                (w, _) => Task.FromResult(new SystemClockAdvancingEvent(Globals.SystemClock.Now.AddHours(1))));
            _simulator.ConfigureCustomEvent(
                100.0,
                w => Task.FromException(new Exception()),
                w => Task.FromResult(false));
            _simulator.RunSimulations(1, 10);
        }

        [Fact]
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
                    Assert.Equal(2, events.Count);
                    Assert.Equal(
                        "Workflow was stopped because maximum number of events were reached",
                        events[0].Name);
                    Assert.Equal("Last workflow state is Due", events[1].Name);
                });
            Assert.Equal(1, counter);
        }

        [Fact]
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
            Assert.Equal(0, action1Counter);
        }

        [Fact]
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
                    Assert.Equal(2, events.Count);
                    Assert.Equal(
                        "Workflow was stopped because maximum number of events were reached",
                        events[0].Name);
                });
            Assert.Equal(4, action1Counter);
            Assert.Equal(1, action3Counter);
        }

        [Fact]
        public void ApplicationRestartEventShouldStopWorkflowAdvanceSystemClockAndRerunWorkflow()
        {
            var clockAdvanced = false;
            WorkflowBase oldWorkflow = null;
            _simulator.ConfigureSystemClockAdvancingEvent(
                0.1,
                (w, isAppRestart) =>
                {
                    clockAdvanced = true;
                    oldWorkflow = w;
                    Assert.True(w.CompletedTask.IsCompleted);
                    Assert.True(isAppRestart);
                    Assert.Equal(1, w.TestId);
                    Assert.Equal(2, w.TransientTestId);
                    return Task.FromResult(
                        new SystemClockAdvancingEvent(Globals.SystemClock.Now.AddHours(1).AddMinutes(23)));
                });
            var customEventCalled = false;
            _simulator.ConfigureCustomEvent(
                100.0,
                w =>
                {
                    customEventCalled = true;
                    Assert.NotNull(oldWorkflow);
                    Assert.NotSame(oldWorkflow, w);
                    Assert.Equal(1, w.TestId);
                    Assert.Equal(2, w.TransientTestId);
                    return Task.CompletedTask;
                });
            _simulator.ConfigureApplicationRestartEvent(100.0);

            var counter = 0;
            _simulator.RunSimulations(
                1,
                6,
                getInitialWorkflowData: () => new Dictionary<string, object> { ["TestId"] = ++counter },
                getInitialWorkflowTransientData: () => new Dictionary<string, object> { ["TransientTestId"] = ++counter },
                randomGeneratorSeed: 171787817,
                afterSimulationCallback: () =>
                {
                    var events = Globals.EventMonitor.GetEvents();
                    Assert.Equal(22, events.Count);
                    Assert.Equal("Application is shutdown", events[0].Name);
                    Assert.Equal("Workflow was stopped due to application shutdown", events[1].Name);
                    Assert.Equal("System clock is advanced", events[2].Name);
                    Assert.Equal("Application is started", events[3].Name);
                });
            Assert.True(clockAdvanced);
            Assert.True(customEventCalled);
        }

        [Fact]
        public void IfWorkflowIsFailedThenSimulationShouldStopAndItsSequenceOfEventsShouldBeReported()
        { // TODO: Unstable
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
                _output.WriteLine(stats.ToString());
                Assert.True(false);
            }
            catch (SimulationException ex)
            {
                _output.WriteLine(ex.ToString());
                Assert.Equal(6, ex.Events.Count);
                Assert.Equal("Custom event", ex.Events[0].Name);
                Assert.Equal("Action 4", ex.Events[5].Name);
            }
        }

        [Fact]
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
                Assert.True(false);
            }
            catch (SimulationException ex)
            {
                _output.WriteLine(ex.ToString());
                Assert.Equal(1, ex.Events.Count);
                Assert.Equal("Custom event", ex.Events[0].Name);
            }
        }

        [Fact]
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
                Assert.True(false);
            }
            catch (SimulationException ex)
            {
                _output.WriteLine(ex.ToString());
                Assert.True(ex.Events.Count >= 6 && ex.Events.Count <= 7);
            }
        }

        [Fact]
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
            Assert.NotNull(stats);
            _output.WriteLine(stats.ToString());
            Assert.Equal(5, stats.NumberOfSimulations);
            Assert.Equal(35, stats.TotalNumberOfEvents);
            Assert.Equal(7.0, stats.AverageNumberOfEventsPerSimulation);
            Assert.Equal(action2Counter, stats.Events[0].Item2);
            Assert.Equal(action1Counter, stats.Events[1].Item2);
        }

        [Fact]
        public void SimulationShouldFailIfMaxEventProcessingTimeIsExceeded()
        {
            _simulator.ConfigureCustomEvent(1.0, w => Task.Delay(100));
            var ex = Record.Exception(() => _simulator.RunSimulations(1, 5, maxEventProcessingTime: 10));

            Assert.IsType<SimulationException>(ex);
        }

        [Fact]
        public void IfMaxConcurrentEventsIsGreaterThen1ThenEventsShouldBeRunConcurrently()
        {
            _simulator = new MonteCarloSimulator<TestWorkflow>(
                () => _lastWorkflow = new TestWorkflow(),
                maxConcurrentEvents: 5);

            var action1Counter = 0;
            _simulator.ConfigureCustomEvent(
                1.0,
                (w, concurrentEvents) =>
                {
                    Assert.True(concurrentEvents);
                    Globals.EventMonitor.LogEvent("Custom event 1");
                    Interlocked.Increment(ref action1Counter);
                    return Task.CompletedTask;
                });
            var action2Counter = 0;
            _simulator.ConfigureCustomEvent(
                2.0,
                w =>
                {
                    Globals.EventMonitor.LogEvent("Custom event 2");
                    Interlocked.Increment(ref action2Counter);
                    return Task.CompletedTask;
                },
                isSequential: true);

            var stats = _simulator.RunSimulations(
                5,
                5000,
                randomGeneratorSeed: 171787817,
                getEventsAvailabilityAwaiter: w => new WorkflowActionsAvailabilityAwaiter<States>(w));
            Assert.NotNull(stats);
            _output.WriteLine(stats.ToString());
            Assert.True(action1Counter > action2Counter);
        }

        [Fact]
        public async Task WorkflowActionsAvailabilityAwaiterShouldWaitForWorkflowStateChange()
        {
            Utilities.SystemClock = new TestingSystemClock();
            var workflow = new TestWorkflow();
            workflow.StartWorkflow(initialWorkflowData: new Dictionary<string, object> { ["NoActions"] = true });
            await workflow.StateEventTask; // We cannot wait for StateInitializedTask because it is completed before StateChanged event

            var t = new WorkflowActionsAvailabilityAwaiter<States>(workflow).Task;
            Assert.False(t.IsCompleted);
            TestingSystemClock.Current.Add(TimeSpan.FromHours(17));

            await t;

            var state = await workflow.GetStateAsync();
            Assert.Equal(States.Due, state);
        }

        [Fact]
        public async Task WorkflowActionsAvailabilityAwaiterShouldCompleteIfWorkflowIsStoppedOrCompleted()
        {
            Utilities.SystemClock = new TestingSystemClock();
            var workflow = new TestWorkflow();
            workflow.StartWorkflow(
                initialWorkflowData: new Dictionary<string, object> { ["NoActions"] = true, ["CompleteFast"] = true });
            await workflow.StartedTask;

            var state = await workflow.GetTransientDataFieldAsync<States>("State", forceExecution: true);
            Assert.Equal(States.Contacted, state);
            await new WorkflowActionsAvailabilityAwaiter<States>(workflow).Task;

            Assert.Equal(TaskStatus.RanToCompletion, workflow.CompletedTask.Status);
        }

        [Fact]
        public void EventsAvailabilityAwaiterShouldBeAwaitedBeforeGeneratingEventsInNonPrimaryPartitions()
        {
            _simulator =
                new MonteCarloSimulator<TestWorkflow>(
                    () => _lastWorkflow = new TestWorkflow(),
                    maxConcurrentEvents: 2);

            _simulator.ConfigureSystemClockAdvancingEvent(
                0.01,
                (w, isAppRestart) =>
                    Task.FromResult(new SystemClockAdvancingEvent(Globals.SystemClock.Now.AddHours(1))));

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
            Assert.NotNull(stats);
            _output.WriteLine(stats.ToString());
        }

        private class TestWorkflow : WorkflowBase<States>
        {
            private readonly TaskCompletionSource<bool> _stateEventTcs = new TaskCompletionSource<bool>();

            public Task StateEventTask => _stateEventTcs.Task;

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField]
            public int TestId { get; set; }

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField(IsTransient = true)]
            public int TransientTestId { get; set; }

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField]
            private bool NoActions { get; set; }

            // ReSharper disable once UnusedAutoPropertyAccessor.Local
            [DataField]
            private bool CompleteFast { get; set; }

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
                StateChanged += (sender, args) => _stateEventTcs.TrySetResult(true);
            }

            protected override bool IsActionAllowed(string action, NamedValues parameters)
            {
                if (State != States.Due)
                {
                    return false;
                }

                return new[] { "Action 1", "Action 3", "Action 4" }.Contains(action);
            }

            protected override async Task RunAsync()
            {
                var date = Globals.SystemClock.Now.AddHours(17);
                SetState(!NoActions ? States.Due : States.Contacted);
                await this.WaitForAny(
                    () => Task.Delay(CompleteFast ? 1 : 10000, Utilities.CurrentCancellationToken),
                    async () =>
                    {
                        await this.WaitForAction("Action 4");
                        throw new NullReferenceException();
                    },
                    () => this.Optional(WaitForDueDate(date)));
            }

            private async Task WaitForDueDate(DateTime date)
            {
                await this.WaitForDate(date);
                SetState(States.Due);
            }
        }
    }
}
