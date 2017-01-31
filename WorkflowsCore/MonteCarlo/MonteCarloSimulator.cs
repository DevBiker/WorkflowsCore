using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;

namespace WorkflowsCore.MonteCarlo
{
    public interface IEventsAvailabilityAwaiter
    {
        Task Task { get; }

        void Cancel();
    }

    public struct WorldClockAdvancingEvent
    {
        public WorldClockAdvancingEvent(DateTime newTime, Task timeChangeProcessed = null)
        {
            NewTime = newTime;
            TimeChangeProcessed = timeChangeProcessed ?? Task.CompletedTask;
        }

        public DateTime NewTime { get; }

        public Task TimeChangeProcessed { get; }
    }

    public class WorkflowActionsAvailabilityAwaiter<TState> : IEventsAvailabilityAwaiter
    {
        private readonly WorkflowBase<TState> _workflow;
        private readonly TaskCompletionSource<bool> _tcs;

        public WorkflowActionsAvailabilityAwaiter(WorkflowBase<TState> workflow)
        {
            _workflow = workflow;
            _tcs = new TaskCompletionSource<bool>();
            _workflow.StateChanged += WorkflowOnStateChanged;
        }

        public Task Task => Task.WhenAny(_tcs.Task, _workflow.CompletedTask.ContinueWith(_ => { })).Unwrap();

        public void Cancel()
        {
            _workflow.StateChanged -= WorkflowOnStateChanged;
            _tcs.TrySetCanceled();
        }

        private void WorkflowOnStateChanged(
            object sender,
            WorkflowBase<TState>.StateChangedEventArgs stateChangedEventArgs)
        {
            _workflow.StateChanged -= WorkflowOnStateChanged;
            _tcs.TrySetResult(true);
        }
    }

    public class MonteCarloSimulator<TWorkflowType>
        where TWorkflowType : WorkflowBase
    {
        private readonly Func<TWorkflowType> _workflowFactory;
        private readonly int _maxConcurrentEvents;
        private readonly IList<EventDefinition> _eventsDefinitions = new List<EventDefinition>();

        private int _currentEventId;

        public MonteCarloSimulator(Func<TWorkflowType> workflowFactory, int maxConcurrentEvents = 1)
        {
            if (maxConcurrentEvents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentEvents));
            }

            _workflowFactory = workflowFactory;
            _maxConcurrentEvents = maxConcurrentEvents;
        }

        public static void InitializeWorkflow<TState>(WorkflowBase<TState> workflow)
        {
            workflow.StateChanged += (sender, args) =>
            {
                Globals.EventMonitor.LogEvent($"State is changed to {args.NewState}");
            };
        }

        public void ConfigureApplicationRestartEvent(double probabilityWeight)
        {
            if (_eventsDefinitions.Any(e => e is ApplicationRestartEventDefintion))
            {
                throw new InvalidOperationException();
            }

            var worldClockAdvancingEventDefinition =
                _eventsDefinitions.OfType<WorldClockAdvancingEventDefinition>().First();
            _eventsDefinitions.Add(
                new ApplicationRestartEventDefintion(
                    probabilityWeight,
                    worldClockAdvancingEventDefinition,
                    _workflowFactory));
        }

        public void ConfigureActionExecutionEvent(double probabilityWeight)
        {
            if (_eventsDefinitions.Any(e => e is ActionExecutionEventDefinition))
            {
                throw new InvalidOperationException();
            }

            _eventsDefinitions.Add(new ActionExecutionEventDefinition(probabilityWeight, _maxConcurrentEvents > 1));
        }

        public void ConfigureActionExecutionEvent(
            double probabilityWeight,
            string actionName,
            Func<TWorkflowType, bool, Task> actionEventHandler)
        {
            var actionExecutionEvent = _eventsDefinitions.OfType<ActionExecutionEventDefinition>().First();
            actionExecutionEvent.AddAction(probabilityWeight, actionName, actionEventHandler);
        }

        public void ConfigureActionExecutionEvent(
            double probabilityWeight,
            string actionName,
            Func<TWorkflowType, Task> actionEventHandler)
        {
            var actionExecutionEvent = _eventsDefinitions.OfType<ActionExecutionEventDefinition>().First();
            actionExecutionEvent.AddAction(probabilityWeight, actionName, (w, _) => actionEventHandler(w));
        }

        public void ConfigureWorldClockAdvancingEvent(
            double probabilityWeight,
            Func<TWorkflowType, bool, Task<WorldClockAdvancingEvent>> getNewTimeEventFunc)
        {
            if (_eventsDefinitions.Any(e => e is WorldClockAdvancingEventDefinition))
            {
                throw new InvalidOperationException();
            }

            _eventsDefinitions.Add(new WorldClockAdvancingEventDefinition(probabilityWeight, getNewTimeEventFunc));
        }

        public void ConfigureCustomEvent(
            double probabilityWeight,
            Func<TWorkflowType, bool, Task> customEventHandler,
            Func<TWorkflowType, Task<bool>> customEventIsAvailablePredicate = null,
            bool isSequential = false)
        {
            _eventsDefinitions.Add(
                new CustomEventDefinition(
                    probabilityWeight,
                    isSequential,
                    _maxConcurrentEvents > 1,
                    customEventHandler,
                    customEventIsAvailablePredicate));
        }

        public void ConfigureCustomEvent(
            double probabilityWeight,
            Func<TWorkflowType, Task> customEventHandler,
            Func<TWorkflowType, Task<bool>> customEventIsAvailablePredicate = null,
            bool isSequential = false)
        {
            _eventsDefinitions.Add(
                new CustomEventDefinition(
                    probabilityWeight,
                    isSequential,
                    _maxConcurrentEvents > 1,
                    (w, _) => customEventHandler(w),
                    customEventIsAvailablePredicate));
        }

        public SimulationsStats RunSimulations(
            int numberOfSimulations,
            int maxNumberOfEventsPerSimulation,
            int? randomGeneratorSeed = null,
            Func<IReadOnlyDictionary<string, object>> getInitialWorkflowData = null,
            Func<IReadOnlyDictionary<string, object>> getInitialWorkflowTransientData = null,
            Action beforeSimulationCallback = null,
            Action afterSimulationCallback = null,
            int maxConcurrentSimulations = -1,
            int maxEventProcessingTime = 1000,
            Func<TWorkflowType, IEventsAvailabilityAwaiter> getEventsAvailabilityAwaiter = null)
        {
            if (numberOfSimulations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numberOfSimulations));
            }

            if (maxNumberOfEventsPerSimulation <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxNumberOfEventsPerSimulation));
            }

            if (!_eventsDefinitions.Any())
            {
                throw new InvalidOperationException("No events were configured");
            }

            var actionExecutionEvent = _eventsDefinitions.OfType<ActionExecutionEventDefinition>().FirstOrDefault();
            if (!(actionExecutionEvent?.HasActions ?? true))
            {
                throw new InvalidOperationException("No specific actions were configured");
            }

            if (_maxConcurrentEvents > 1)
            {
                if (getEventsAvailabilityAwaiter == null)
                {
                    throw new ArgumentNullException(nameof(getEventsAvailabilityAwaiter));
                }

                maxConcurrentSimulations = 1;
            }

            Globals.RandomGenerator = new RandomGenerator(randomGeneratorSeed);
            try
            {
                SimulationException simulationException = null;
                var results = new EventMonitor[numberOfSimulations];

                Parallel.For(
                    0,
                    numberOfSimulations,
                    new ParallelOptions { MaxDegreeOfParallelism = maxConcurrentSimulations },
                    (i, loopState) =>
                    {
                        try
                        {
                            results[i] = RunSimulation(
                                maxNumberOfEventsPerSimulation,
                                maxEventProcessingTime,
                                () => loopState.ShouldExitCurrentIteration,
                                beforeSimulationCallback,
                                getInitialWorkflowData,
                                getInitialWorkflowTransientData,
                                afterSimulationCallback,
                                getEventsAvailabilityAwaiter).Result;
                        }
                        catch (AggregateException ex) when (ex.GetBaseException() is SimulationException)
                        {
                            loopState.Stop();
                            Interlocked.CompareExchange(
                                ref simulationException,
                                (SimulationException)ex.GetBaseException(),
                                null);
                        }
                        catch (Exception)
                        {
                            loopState.Stop();
                            throw;
                        }
                    });

                if (simulationException != null)
                {
                    throw simulationException;
                }

                return new SimulationsStats(Globals.RandomGenerator.Seed, results);
            }
            finally
            {
                Globals.RandomGenerator = null;
            }
        }

        private static void VerifyProbabilityWeight(double probabilityWeight)
        {
            if (probabilityWeight <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(probabilityWeight));
            }
        }

        private static async Task ProcessWorkflowCompletion(TWorkflowType workflow, Exception exception)
        {
            await workflow.DoWorkflowTaskAsync(() => { }, true); // Wait until workflow finishes current work

            if (!workflow.CompletedTask.IsCompleted)
            {
                workflow.StopWorkflow(
                    exception ??
                        new HelperException("Workflow was stopped because maximum number of events were reached"));
            }

            try
            {
                await workflow.CompletedTask;

                if (exception == null)
                {
                    Globals.EventMonitor.LogEvent("Workflow completed");
                }
            }
            catch (HelperException ex)
            {
                // If current workflow was stopped due to application shutdown then it is old workflow from previous application instance.
                // It was not replaced with new workflow due to some exception during new workflow state restoring
                if (
                    !string.Equals(
                        ex.Message,
                        ApplicationRestartEventDefintion.WorkflowWasStoppedDueToApplicationShutdown,
                        StringComparison.Ordinal))
                {
                    Globals.EventMonitor.LogEvent(ex.Message);
                    var lastWorkflowState =
                        (await workflow.GetTransientDataFieldAsync<object>("State", forceExecution: true))?.ToString() ??
                            "N/A";
                    Globals.EventMonitor.LogEvent($"Last workflow state is {lastWorkflowState}");
                }
            }
            catch (AggregateException ex)
            {
                throw new SimulationException(
                    Globals.RandomGenerator.Seed,
                    Globals.EventMonitor.GetEvents(),
                    new AggregateException(ex.InnerExceptions.Where(e => !(e is HelperException))));
            }
            catch (Exception ex)
            {
                throw new SimulationException(Globals.RandomGenerator.Seed, Globals.EventMonitor.GetEvents(), ex);
            }

            if (exception != null)
            {
                throw new SimulationException(Globals.RandomGenerator.Seed, Globals.EventMonitor.GetEvents(), exception);
            }
        }

        private async Task<EventMonitor> RunSimulation(
            int maxNumberOfEventsPerSimulation,
            int maxEventProcessingTime,
            Func<bool> shouldStopSimulation,
            Action beforeSimulationCallback, // TODO: Accept workflow
            Func<IReadOnlyDictionary<string, object>> getInitialWorkflowData,
            Func<IReadOnlyDictionary<string, object>> getInitialWorkflowTransientData,
            Action afterSimulationCallback, // TODO: Accept workflow
            Func<TWorkflowType, IEventsAvailabilityAwaiter> getEventsAvailabilityAwaiter)
        {
            Globals.EventMonitor = new EventMonitor();
            Utilities.TimeProvider = new TestingTimeProvider();
            Globals.EventId = Interlocked.Increment(ref _currentEventId);
            beforeSimulationCallback?.Invoke();
            try
            {
                var workflow = _workflowFactory();
                var initialWorkflowData = getInitialWorkflowData?.Invoke();
                var initialWorkflowTransientData = getInitialWorkflowTransientData?.Invoke();
                workflow.StartWorkflow(
                    initialWorkflowData: initialWorkflowData,
                    initialWorkflowTransientData: initialWorkflowTransientData);

                Exception exception = null;
                var partitioner = Partitioner.Create(
                    0,
                    maxNumberOfEventsPerSimulation,
                    (maxNumberOfEventsPerSimulation + _maxConcurrentEvents - 1) / _maxConcurrentEvents);
                Parallel.ForEach(
                    partitioner,
                    new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrentEvents },
                    (range, loopState) =>
                    {
                        try
                        {
                            GenerateAndDoEvents(
                                range,
                                () => shouldStopSimulation() || loopState.ShouldExitCurrentIteration,
                                maxEventProcessingTime,
                                workflow,
                                (oldWorkflow, newWorkflow) =>
                                {
                                    Interlocked.CompareExchange(ref workflow, newWorkflow, oldWorkflow);
                                    return workflow;
                                },
                                getEventsAvailabilityAwaiter).Wait();
                        }
                        catch (Exception ex)
                        {
                            loopState.Stop();
                            Interlocked.CompareExchange(ref exception, ex, null);
                        }
                    });

                await ProcessWorkflowCompletion(workflow, exception);

                return Globals.EventMonitor;
            }
            finally
            {
                Globals.EventMonitor.SimulationCompleted();
                afterSimulationCallback?.Invoke();
                Globals.EventMonitor = null;
                Utilities.TimeProvider = null;
            }
        }

        private async Task GenerateAndDoEvents(
            Tuple<int, int> range,
            Func<bool> shouldStopSimulation,
            int maxEventProcessingTime,
            TWorkflowType workflow,
            Func<TWorkflowType, TWorkflowType, TWorkflowType> updateWorkflow,
            Func<TWorkflowType, IEventsAvailabilityAwaiter> getEventsAvailabilityAwaiter)
        {
            await workflow.StartedTask.WaitWithTimeout(
                maxEventProcessingTime,
                $"[{Globals.EventId}] Timeout on workflow start");

            var isPrimaryPartion = range.Item1 == 0;
            for (var i = range.Item1;
                i < range.Item2 && !workflow.CompletedTask.IsCompleted && !shouldStopSimulation();
                ++i)
            {
                try
                {
                    var newWorkflow = await GenerateAndDoEvent(
                        workflow,
                        isPrimaryPartion,
                        getEventsAvailabilityAwaiter,
                        maxEventProcessingTime);

                    workflow = updateWorkflow(workflow, newWorkflow);
                }
                catch (TaskCanceledException)
                {
                    workflow = updateWorkflow(workflow, workflow); // Old workflow may be stopped, get new one
                }
            }
        }

        private async Task<TWorkflowType> GenerateAndDoEvent(
            TWorkflowType workflow,
            bool isPrimaryPartition,
            Func<TWorkflowType, IEventsAvailabilityAwaiter> getEventsAvailabilityAwaiter,
            int maxEventProcessingTime)
        {
            var eventDef = await GetNextEvent(workflow, isPrimaryPartition, getEventsAvailabilityAwaiter)
                .WaitWithTimeout(maxEventProcessingTime, $"[{Globals.EventId}] Timeout on retrieving next event");
            Globals.EventId = Interlocked.Increment(ref _currentEventId);
            return await eventDef.DoEvent(workflow)
                .WaitWithTimeout(maxEventProcessingTime, $"[{Globals.EventId}] Timeout on executing event");
        }

        private async Task<EventDefinition> GetNextEvent(
            TWorkflowType workflow,
            bool isPrimaryPartition,
            Func<TWorkflowType, IEventsAvailabilityAwaiter> getEventsAvailabilityAwaiter)
        {
            IList<EventDefinition> availableEvents;
            while (true)
            {
                var eventsAwaiter = isPrimaryPartition ? null : getEventsAvailabilityAwaiter(workflow);
                availableEvents = await GetAvailableEvents(workflow, isPrimaryPartition);
                if (isPrimaryPartition)
                {
                    break;
                }

                if (!availableEvents.Any())
                {
                    await eventsAwaiter.Task;
                }
                else
                {
                    eventsAwaiter.Cancel();
                    break;
                }
            }

            var index = Globals.RandomGenerator.GetRandomItemIndexFromProbabilityWeights(
                availableEvents.Select(e => e.ProbabilityWeight).ToList());
            return availableEvents[index];
        }

        private async Task<IList<EventDefinition>> GetAvailableEvents(TWorkflowType workflow, bool isPrimaryPartition)
        {
            var availabilities =
                await Task.WhenAll(_eventsDefinitions.Select(e => e.GetIsAvailable(workflow, isPrimaryPartition)));
            var availableEvents =
                _eventsDefinitions.Select((e, i) => availabilities[i] ? e : null).Where(e => e != null).ToList();
            return availableEvents;
        }

        private abstract class EventDefinition
        {
            private readonly bool _isSequential;

            protected EventDefinition(double probabilityWeight, bool isSequential = true)
            {
                _isSequential = isSequential;
                VerifyProbabilityWeight(probabilityWeight);

                ProbabilityWeight = probabilityWeight;
            }

            public double ProbabilityWeight { get; }

            public virtual Task<bool> GetIsAvailable(TWorkflowType workflow, bool isPrimaryPartition) => 
                Task.FromResult(GetIsAvailableCore(isPrimaryPartition));

            public abstract Task<TWorkflowType> DoEvent(TWorkflowType workflow);

            protected bool GetIsAvailableCore(bool isPrimaryPartition) => !_isSequential || isPrimaryPartition;
        }

        private class WorldClockAdvancingEventDefinition : EventDefinition
        {
            private readonly Func<TWorkflowType, bool, Task<WorldClockAdvancingEvent>> _getNewTimeEventFunc;

            public WorldClockAdvancingEventDefinition(
                double probabilityWeight,
                Func<TWorkflowType, bool, Task<WorldClockAdvancingEvent>> getNewTimeEventFunc)
                : base(probabilityWeight)
            {
                _getNewTimeEventFunc = getNewTimeEventFunc;
            }

            public override Task<TWorkflowType> DoEvent(TWorkflowType workflow) => DoEvent(workflow, false);

            public async Task<TWorkflowType> DoEvent(TWorkflowType workflow, bool isApplicationRestart)
            {
                var newTimeEvent = await _getNewTimeEventFunc(workflow, isApplicationRestart);
                TestingTimeProvider.Current.SetCurrentTime(newTimeEvent.NewTime);
                Globals.EventMonitor.LogEvent(
                    "World clock is advanced",
                    Globals.TimeProvider.Now.ToString(Globals.DefaultDateTimeFormat));
                await newTimeEvent.TimeChangeProcessed;
                return workflow;
            }
        }

        private class CustomEventDefinition : EventDefinition
        {
            private readonly bool _concurrentEvents;
            private readonly Func<TWorkflowType, bool, Task> _customEventHandler;
            private readonly Func<TWorkflowType, Task<bool>> _customEventIsAvailablePredicate;

            public CustomEventDefinition(
                double probabilityWeight,
                bool isSequential,
                bool concurrentEvents,
                Func<TWorkflowType, bool, Task> customEventHandler,
                Func<TWorkflowType, Task<bool>> customEventIsAvailablePredicate)
                : base(probabilityWeight, isSequential)
            {
                _concurrentEvents = concurrentEvents;
                _customEventHandler = customEventHandler;
                _customEventIsAvailablePredicate = customEventIsAvailablePredicate;
            }

            public override Task<bool> GetIsAvailable(TWorkflowType workflow, bool isPrimaryPartition) =>
                !GetIsAvailableCore(isPrimaryPartition)
                    ? Task.FromResult(false)
                    : _customEventIsAvailablePredicate?.Invoke(workflow) ?? Task.FromResult(true);

            public override async Task<TWorkflowType> DoEvent(TWorkflowType workflow)
            {
                await _customEventHandler(workflow, _concurrentEvents);
                return workflow;
            }
        }

        private class ApplicationRestartEventDefintion : EventDefinition
        {
            public const string WorkflowWasStoppedDueToApplicationShutdown =
                "Workflow was stopped due to application shutdown";

            private readonly WorldClockAdvancingEventDefinition _worldClockAdvancingEventDefinition;
            private readonly Func<TWorkflowType> _workflowFactory;

            // TODO: Add ability to change something additionally to time
            public ApplicationRestartEventDefintion(
                double probabilityWeight,
                WorldClockAdvancingEventDefinition worldClockAdvancingEventDefinition,
                Func<TWorkflowType> workflowFactory)
                : base(probabilityWeight)
            {
                _worldClockAdvancingEventDefinition = worldClockAdvancingEventDefinition;
                _workflowFactory = workflowFactory;
            }

            public override async Task<TWorkflowType> DoEvent(TWorkflowType workflow)
            {
                Globals.EventMonitor.LogEvent("Application is shutdown");
                if (!workflow.CompletedTask.IsCompleted)
                {
                    workflow.StopWorkflow(new HelperException(WorkflowWasStoppedDueToApplicationShutdown));
                }

                try
                {
                    await workflow.CompletedTask;
                }
                catch (HelperException ex)
                {
                    Globals.EventMonitor.LogEvent(ex.Message);
                }

                await _worldClockAdvancingEventDefinition.DoEvent(workflow, true);
                var statesHistory =
                    await workflow.TryGetDataFieldAsync<IEnumerable>("StatesHistory", forceExecution: true) ??
                        Enumerable.Empty<object>();
                Globals.EventMonitor.LogEvent(
                    "Application is started",
                    $"StatesHistory: [{string.Join(", ", statesHistory.Cast<object>())}]");

                var newWorkflow = _workflowFactory();
                Dictionary<string, object> data = null;
                Dictionary<string, object> transientData = null;
                await workflow.DoWorkflowTaskAsync(
                    w =>
                    {
                        data = w.Metadata.GetData(w);
                        transientData = w.Metadata.GetTransientData(w);
                    },
                    forceExecution: true);
                newWorkflow.StartWorkflow(loadedWorkflowData: data, initialWorkflowTransientData: transientData);
                try
                {
                    await newWorkflow.StartedTask;
                }
                catch (TaskCanceledException)
                {
                    await newWorkflow.CompletedTask; // Most probably workflow was stopped due to exception, so rethrow it
                }
                
                return newWorkflow;
            }
        }

        private class ActionExecutionEventDefinition : EventDefinition
        {
            private readonly bool _concurrentEvents;

            private readonly IDictionary<string, ActionEventDefinition> _actions =
                new Dictionary<string, ActionEventDefinition>();

            public ActionExecutionEventDefinition(double probabilityWeight, bool concurrentEvents) 
                : base(probabilityWeight, false)
            {
                _concurrentEvents = concurrentEvents;
            }

            public bool HasActions => _actions.Any();

            public void AddAction(
                double probabilityWeight,
                string actionName,
                Func<TWorkflowType, bool, Task> actionEventHandler)
            {
                VerifyProbabilityWeight(probabilityWeight);
                if (_actions.ContainsKey(actionName))
                {
                    throw new InvalidOperationException();
                }

                var actionDefinition = new ActionEventDefinition
                {
                    ProbabilityWeight = probabilityWeight,
                    ActionEventHandler = actionEventHandler
                };
                _actions.Add(actionName, actionDefinition);
            }

            public override async Task<bool> GetIsAvailable(TWorkflowType workflow, bool isPrimaryPartition)
            {
                var availableActions = await GetAvailableActionsAsync(workflow);
                return availableActions.Any();
            }

            public override async Task<TWorkflowType> DoEvent(TWorkflowType workflow)
            {
                var actions = await GetAvailableActionsAsync(workflow);
                if (_concurrentEvents && !actions.Any())
                {
                    Globals.EventMonitor.LogEvent(
                        "Workflow action execution is skipped due to no actions are available");
                    return workflow;
                }

                var index = Globals.RandomGenerator.GetRandomItemIndexFromProbabilityWeights(
                    actions.Select(d => d.ProbabilityWeight).ToList());

                var action = actions[index];
                await action.ActionEventHandler(workflow, _concurrentEvents);
                return workflow;
            }

            private async Task<IList<ActionEventDefinition>> GetAvailableActionsAsync(TWorkflowType workflow)
            {
                var availableActions = await workflow.GetAvailableActionsAsync();
                return availableActions.Select(
                    a =>
                    {
                        ActionEventDefinition definition1;
                        return !_actions.TryGetValue(a, out definition1) ? null : definition1;
                    }).Where(d => d != null).ToList();
            }
        }

        private class ActionEventDefinition
        {
            public double ProbabilityWeight { get; set; }

            public Func<TWorkflowType, bool, Task> ActionEventHandler { get; set; }
        }

        private class HelperException : Exception
        {
            public HelperException(string message) 
                : base(message)
            {
            }
        }
    }
}
