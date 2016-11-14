using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;

namespace WorkflowsCore
{
    public abstract class WorkflowBase
    {
        private readonly Func<IWorkflowStateRepository> _workflowRepoFactory;

        private readonly Lazy<WorkflowMetadata> _metadata;

        private readonly TaskCompletionSource<bool> _startedTaskCompletionSource =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _completedTaskCompletionSource =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly NamedValues _data = new NamedValues();
        private readonly NamedValues _transientData = new NamedValues();
        private readonly IList<string> _actions = new List<string>(); 
        private readonly IDictionary<string, ActionDefinition> _actionsDefinitions = 
            new Dictionary<string, ActionDefinition>();

        private readonly ConcurrentExclusiveSchedulerPair _concurrentExclusiveSchedulerPair;

        private object _id;
        private Exception _exception;
        private bool _wasStared;

        protected WorkflowBase()
            : this(null, true)
        {
        }

        protected WorkflowBase(Func<IWorkflowStateRepository> workflowRepoFactory)
            : this(workflowRepoFactory, true)
        {
        }

        protected WorkflowBase(
            Func<IWorkflowStateRepository> workflowRepoFactory,
            bool isStateInitializedImmediatelyAfterStart)
        {
            _metadata = new Lazy<WorkflowMetadata>(
                () => Utilities.WorkflowMetadataCache.GetWorkflowMetadata(this),
                LazyThreadSafetyMode.PublicationOnly);
            _concurrentExclusiveSchedulerPair = Utilities.WorkflowsTaskScheduler == null
                ? new ConcurrentExclusiveSchedulerPair()
                : new ConcurrentExclusiveSchedulerPair(Utilities.WorkflowsTaskScheduler);
            _workflowRepoFactory = workflowRepoFactory ?? (() => new DummyWorkflowStateRepository());
            if (isStateInitializedImmediatelyAfterStart)
            {
                StateInitializedTask = StartedTask;
            }
            else
            {
                SetTransientData(
                    nameof(StateInitializedTaskCompletionSource),
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
                StateInitializedTask = StateInitializedTaskCompletionSource.Task;
            }

            CancellationTokenSource = new CancellationTokenSource();
        }

        protected internal event EventHandler<ActionExecutedEventArgs> ActionExecuted;

        public object Id
        {
            get
            {
                return _id;
            }

            set
            {
                var oldValue = Interlocked.CompareExchange(ref _id, value, null);
                if (ReferenceEquals(_id, oldValue))
                {
                    throw new InvalidOperationException();
                }
            }
        }

        public WorkflowMetadata Metadata => _metadata.Value;

        public Task StartedTask => _startedTaskCompletionSource.Task;

        public Task StateInitializedTask { get; }

        public Task CompletedTask => _completedTaskCompletionSource.Task;

        public bool IsWorkflowTaskScheduler => TaskScheduler.Current == WorkflowTaskScheduler;

        internal IReadOnlyDictionary<string, object> Data => _data.Data;

        internal IReadOnlyDictionary<string, object> TransientData => _transientData.Data;

        protected static ITimeProvider TimeProvider => Utilities.TimeProvider;

        private CancellationTokenSource CancellationTokenSource { get; }

        private TaskScheduler WorkflowTaskScheduler => _concurrentExclusiveSchedulerPair.ExclusiveScheduler;

        private TaskCompletionSource<bool> StateInitializedTaskCompletionSource =>
            GetTransientData<TaskCompletionSource<bool>>(nameof(StateInitializedTaskCompletionSource));

        private IDictionary<string, int> ActionStats => GetData<IDictionary<string, int>>(nameof(ActionStats));

        public void StartWorkflow(
            object id = null,
            IReadOnlyDictionary<string, object> initialWorkflowData = null,
            IReadOnlyDictionary<string, object> loadedWorkflowData = null,
            Action beforeWorkflowStarted = null,
            Action afterWorkflowFinished = null)
        {
            lock (CancellationTokenSource)
            {
                if (_wasStared)
                {
                    throw new InvalidOperationException();
                }

                _wasStared = true;
                DoWorkflowTaskAsync(
                    w =>
                    {
                        OnInit();
                        if (initialWorkflowData != null)
                        {
                            w.SetData(initialWorkflowData);
                        }

                        var wasCreated = false;
                        if (loadedWorkflowData == null)
                        {
                            wasCreated = true;
                            OnCreated();
                            SaveWorkflowData();
                        }
                        else
                        {
                            if (id != null)
                            {
                                Id = id;
                            }

                            w.SetData(loadedWorkflowData);
                            OnLoaded();
                        }

                        beforeWorkflowStarted?.Invoke();

                        if (!_startedTaskCompletionSource.TrySetResult(true))
                        {
                            return Task.CompletedTask;
                        }

                        if (!wasCreated && ReferenceEquals(StateInitializedTask, StartedTask))
                        {
                            SaveWorkflowData();
                        }

                        return RunAsync();
                    },
                    forceExecution: true).Unwrap().ContinueWith(
                        t =>
                        {
                            var canceled = false;
                            Exception exception = null;
                            try
                            {
                                ProcessWorkflowCompletion(t, afterWorkflowFinished, out exception, out canceled);
                            }
                            catch (Exception ex)
                            {
                                exception = GetAggregatedExceptions(exception, ex);
                            }
                            finally
                            {
                                if (exception != null)
                                {
                                    _completedTaskCompletionSource.SetException(exception);
                                }
                                else if (canceled)
                                {
                                    _completedTaskCompletionSource.SetCanceled();
                                }
                                else
                                {
                                    _completedTaskCompletionSource.SetResult(true);
                                }
                            }
                        },
                        TaskContinuationOptions.RunContinuationsAsynchronously);
            }
        }

        public Task RunViaWorkflowTaskScheduler(Action action, bool forceExecution = false) =>
            RunViaWorkflowTaskScheduler(w => action(), forceExecution);

        public Task<T> RunViaWorkflowTaskScheduler<T>(Func<T> func, bool forceExecution = false) =>
            RunViaWorkflowTaskScheduler(w => func(), forceExecution);

        public Task RunViaWorkflowTaskScheduler(Action<WorkflowBase> action, bool forceExecution = false)
        {
            if (IsWorkflowTaskScheduler)
            {
                action(this);
                return Task.CompletedTask;
            }

            return DoWorkflowTaskAsync(action, forceExecution);
        }

        public Task<T> RunViaWorkflowTaskScheduler<T>(Func<WorkflowBase, T> func, bool forceExecution = false)
        {
            if (IsWorkflowTaskScheduler)
            {
                return Task.FromResult(func(this));
            }

            return DoWorkflowTaskAsync(func, forceExecution);
        }

        public void EnsureWorkflowTaskScheduler()
        {
            if (!IsWorkflowTaskScheduler)
            {
                throw new InvalidOperationException("This operation cannot be used outside of workflow task scheduler");
            }
        }

        public Task DoWorkflowTaskAsync(Action action, bool forceExecution = false) =>
            DoWorkflowTaskAsync(w => action(), forceExecution);

        public Task<T> DoWorkflowTaskAsync<T>(Func<T> func, bool forceExecution = false) =>
            DoWorkflowTaskAsync(w => func(), forceExecution);

        public Task DoWorkflowTaskAsync(Action<WorkflowBase> action, bool forceExecution = false)
        {
            return Utilities.SetCurrentCancellationTokenTemporarily(
                CancellationTokenSource.Token,
                () => Task.Factory.StartNew(
                    async () =>
                    {
                        if (!forceExecution)
                        {
                            await StartedTask;
                        }

                        action(this);
                    },
                    forceExecution ? CancellationToken.None : Utilities.CurrentCancellationToken,
                    TaskCreationOptions.PreferFairness,
                    WorkflowTaskScheduler)).Unwrap();
        }

        public Task<T> DoWorkflowTaskAsync<T>(Func<WorkflowBase, T> func, bool forceExecution = false)
        {
            return Utilities.SetCurrentCancellationTokenTemporarily(
                CancellationTokenSource.Token,
                () => Task.Factory.StartNew(
                    async () =>
                    {
                        if (!forceExecution)
                        {
                            await StartedTask;
                        }

                        return func(this);
                    },
                    forceExecution ? CancellationToken.None : Utilities.CurrentCancellationToken,
                    TaskCreationOptions.PreferFairness,
                    WorkflowTaskScheduler)).Unwrap();
        }

        public Task<object> GetIdAsync() => DoWorkflowTaskAsync(() => Id);

        public Task ExecuteActionAsync(string action, bool throwNotAllowed = true) => 
            ExecuteActionAsync<object>(action, new Dictionary<string, object>(), throwNotAllowed);

        public Task ExecuteActionAsync(
            string action,
            IReadOnlyDictionary<string, object> parameters,
            bool throwNotAllowed = true)
        {
            return ExecuteActionAsync<object>(action, parameters, throwNotAllowed);
        }

        public Task<T> ExecuteActionAsync<T>(string action, bool throwNotAllowed = true) => 
            ExecuteActionAsync<T>(action, new Dictionary<string, object>(), throwNotAllowed);

        public Task<T> ExecuteActionAsync<T>(
            string action,
            IReadOnlyDictionary<string, object> parameters,
            bool throwNotAllowed = true)
        {
            return DoWorkflowTaskAsync(
                async () =>
                {
                    await StateInitializedTask;
                    return ExecuteAction<T>(action, parameters, throwNotAllowed);
                }).Unwrap();
        }

        public void StopWorkflow(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            Cancel(exception);
        }

        public void CancelWorkflow() => Cancel();

        public Task<IList<string>> GetAvailableActionsAsync()
        {
            return DoWorkflowTaskAsync(
                async () =>
                {
                    await StateInitializedTask;
                    return GetAvailableActions();
                }).Unwrap();
        }

        public Task<T> GetDataFieldAsync<T>(string key, bool forceExecution = false) => 
            RunViaWorkflowTaskScheduler(() => GetData<T>(key), forceExecution);

        internal T GetDataField<T>(string key) => GetData<T>(key);

        internal void SetDataField<T>(string key, T value) => SetData(key, value);

        internal T GetTransientDataField<T>(string key) => GetTransientData<T>(key);

        internal void SetTransientDataField<T>(string key, T value) => SetTransientData(key, value);

        internal void SetData(IReadOnlyDictionary<string, object> newData) => _data.SetData(newData);

        internal void SetTransientData(IReadOnlyDictionary<string, object> newData) => _transientData.SetData(newData);

        internal Task ClearTimesExecutedAsync(string action)
        {
            return DoWorkflowTaskAsync(
                () =>
                {
                    ClearTimesExecuted(action);
                    SaveWorkflowData();
                });
        }

        protected internal bool WasExecuted(string action) => TimesExecuted(action) > 0;

        protected NamedValues GetActionMetadata(string action) => GetActionDefinition(action).Metadata;

        protected void SaveWorkflowData() => _workflowRepoFactory().SaveWorkflowData(this);

        protected virtual void OnInit()
        {
            SetData(nameof(ActionStats), new Dictionary<string, int>());
            OnActionsInit();
        }

        protected virtual void OnActionsInit()
        {
        }

        protected virtual void OnCreated()
        {
        }

        protected virtual void OnLoaded()
        {
        }

        protected abstract Task RunAsync();

        protected virtual void OnCanceled()
        {
        }

        protected void Sleep()
        {
            if (CompletedTask.IsCompleted)
            {
                throw new InvalidOperationException();
            }

            _workflowRepoFactory().MarkWorkflowAsSleeping(this);
        }

        protected void Wake()
        {
            if (CompletedTask.IsCompleted)
            {
                throw new InvalidOperationException();
            }

            _workflowRepoFactory().MarkWorkflowAsInProgress(this);
        }

        protected virtual bool IsActionAllowed(string action) => true;

        protected int TimesExecuted(string action)
        {
            var actionDefinition = GetActionDefinition(action);
            int stats;
            return !ActionStats.TryGetValue(actionDefinition.Synonyms.First(), out stats) ? 0 : stats;
        }

        protected void ClearTimesExecuted(string action) => 
            ActionStats.Remove(GetActionDefinition(action).Synonyms.First());

        protected void SetStateInitialized()
        {
            if (StateInitializedTaskCompletionSource == null)
            {
                throw new InvalidOperationException();
            }

            if (StateInitializedTaskCompletionSource.TrySetResult(true))
            {
                SaveWorkflowData();
            }
        }

        protected void ConfigureAction(
            string action,
            Action actionHandler = null,
            IReadOnlyDictionary<string, object> metadata = null,
            string synonym = null,
            IEnumerable<string> synonyms = null,
            bool isHidden = false)
        {
            ConfigureAction<object>(
                action,
                _ =>
                {
                    actionHandler?.Invoke();
                    return null;
                },
                metadata,
                synonym,
                synonyms,
                isHidden);
        }

        protected void ConfigureAction(
            string action,
            Action<NamedValues> actionHandler,
            IReadOnlyDictionary<string, object> metadata = null,
            string synonym = null,
            IEnumerable<string> synonyms = null,
            bool isHidden = false)
        {
            ConfigureAction<object>(
                action,
                parameters =>
                {
                    actionHandler(parameters);
                    return null;
                },
                metadata,
                synonym,
                synonyms,
                isHidden);
        }

        protected void ConfigureAction<T>(
            string action,
            Func<T> actionHandler,
            IReadOnlyDictionary<string, object> metadata = null,
            string synonym = null,
            IEnumerable<string> synonyms = null,
            bool isHidden = false)
        {
            ConfigureAction(action, _ => actionHandler(), metadata, synonym, synonyms, isHidden);
        }

        protected void ConfigureAction<T>(
            string action,
            Func<NamedValues, T> actionHandler,
            IReadOnlyDictionary<string, object> metadata = null,
            string synonym = null,
            IEnumerable<string> synonyms = null,
            bool isHidden = false)
        {
            var allSynonyms = new List<string> { action };
            if (synonym != null)
            {
                allSynonyms.Add(synonym);
            }

            allSynonyms.AddRange(synonyms ?? new List<string>());
            ConfigureActionCore(
                action,
                actionHandler,
                metadata,
                allSynonyms,
                isHidden: isHidden);

            foreach (var curSynonym in allSynonyms.Skip(1))
            {
                ConfigureActionCore(
                    curSynonym,
                    actionHandler,
                    metadata,
                    allSynonyms,
                    true,
                    isHidden);
            }
        }

        protected T GetData<T>(string key) => _data.GetData<T>(key);

        protected void SetData<T>(string key, T value) => _data.SetData(key, value);

        protected T GetTransientData<T>(string key) => _transientData.GetData<T>(key);

        protected void SetTransientData<T>(string key, T value) => _transientData.SetData(key, value);

        protected IList<string> GetActionSynonyms(string action) => GetActionDefinition(action).Synonyms;

        protected void ExecuteAction(string action, bool throwNotAllowed = true) =>
            ExecuteAction<object>(action, new Dictionary<string, object>(), throwNotAllowed);

        protected T ExecuteAction<T>(string action, bool throwNotAllowed = true) =>
            ExecuteAction<T>(action, new Dictionary<string, object>(), throwNotAllowed);

        protected T ExecuteAction<T>(
            string action,
            IReadOnlyDictionary<string, object> parameters,
            bool throwNotAllowed = true)
        {
            var namedValues = new NamedValues(parameters);
            bool wasExecuted;
            var result = OnExecuteAction(action, namedValues, throwNotAllowed, out wasExecuted);
            if (wasExecuted)
            {
                ActionExecuted?.Invoke(
                    this,
                    new ActionExecutedEventArgs(GetActionSynonyms(action), namedValues));
            }

            return (T)result;
        }

        private static Exception GetAggregatedExceptions(Exception exception, Exception newException) => 
            exception == null ? newException : new AggregateException(exception, newException);

        private void ProcessWorkflowCompletion(
            Task task,
            Action afterWorkflowFinished,
            out Exception exception,
            out bool canceled)
        {
            canceled = false;
            exception = null;
            try
            {
                switch (task.Status)
                {
                    case TaskStatus.RanToCompletion:
                        if (CancellationTokenSource.Token.IsCancellationRequested)
                        {
                            HandleCancellation(out exception, out canceled);
                        }
                        else
                        {
                            _workflowRepoFactory().MarkWorkflowAsCompleted(this);
                        }

                        break;
                    case TaskStatus.Faulted: // ReSharper disable once PossibleNullReferenceException
                        exception = task.Exception.GetBaseException();
                        _workflowRepoFactory().MarkWorkflowAsFailed(this, exception);
                        break;
                    case TaskStatus.Canceled:
                        if (CancellationTokenSource.Token.IsCancellationRequested)
                        {
                            HandleCancellation(out exception, out canceled);
                        }
                        else
                        {
                            exception =
                                new TaskCanceledException("Unexpected cancellation of child activity");
                            _workflowRepoFactory().MarkWorkflowAsFailed(this, exception);
                        }

                        break;
                    default:
                        exception = new ArgumentOutOfRangeException();
                        break;
                }
            }
            catch (Exception ex)
            {
                exception = GetAggregatedExceptions(exception, ex);
            }
            finally
            {
                // Ensure any background child activities are canceled and ignore further workflow actions if not enforced
                Cancel();
                afterWorkflowFinished?.Invoke();
            }
        }

        private void HandleCancellation(out Exception exception, out bool canceled)
        {
            exception = null;
            canceled = false;
            lock (CancellationTokenSource)
            {
                if (_exception != null)
                {
                    _workflowRepoFactory().MarkWorkflowAsFailed(this, _exception);
                    exception = _exception;
                }
                else
                {
                    _workflowRepoFactory().MarkWorkflowAsCanceled(this); // TODO: Add exception parameter here
                    OnCanceled();
                    canceled = true;
                }
            }
        }

        private void Cancel(Exception exception = null)
        {
            lock (CancellationTokenSource)
            {
                _startedTaskCompletionSource.TrySetCanceled();
                StateInitializedTaskCompletionSource?.TrySetCanceled();
                try
                {
                    CancellationTokenSource.Cancel(false);
                }
                catch (Exception ex)
                {
                    exception = GetAggregatedExceptions(exception, ex);
                }

                if (exception != null)
                {
                    _exception = _exception == null ? exception : new AggregateException(_exception, exception);
                }
            }
        }

        private object OnExecuteAction(
            string action, NamedValues parameters, bool throwNotAllowed, out bool wasExecuted)
        {
            var actionDefinition = GetActionDefinition(action);

            if (!IsActionAllowed(action))
            {
                if (throwNotAllowed)
                {
                    throw new InvalidOperationException($"{action} action is not allowed");
                }

                wasExecuted = false;
                return null;
            }

            wasExecuted = true;
            var result = actionDefinition.Handler(parameters);

            int stats;
            var primaryName = actionDefinition.Synonyms.First();
            if (!ActionStats.TryGetValue(primaryName, out stats))
            {
                ActionStats[primaryName] = 0;
            }

            ActionStats[primaryName] = ++stats;
            SaveWorkflowData();

            return result;
        }

        private ActionDefinition GetActionDefinition(string action)
        {
            ActionDefinition actionDefinition;
            if (!_actionsDefinitions.TryGetValue(action, out actionDefinition))
            {
                throw new ArgumentOutOfRangeException(nameof(action), $"Action \"{action}\" is not configured");
            }

            return actionDefinition;
        }

        private bool IsActionHidden(string action) => _actionsDefinitions[action].IsHidden;

        private IList<string> GetAvailableActions() => 
            _actions.Where(a => IsActionAllowed(a) && !IsActionHidden(a)).ToList();

        private void ConfigureActionCore<T>(
            string action,
            Func<NamedValues, T> actionHandler,
            IReadOnlyDictionary<string, object> metadata,
            IList<string> synonyms,
            bool isSynonym = false,
            bool isHidden = false)
        {
            ActionDefinition actionDefinition;
            if (_actionsDefinitions.TryGetValue(action, out actionDefinition))
            {
                throw new InvalidOperationException();
            }

            _actionsDefinitions[action] = new ActionDefinition
            {
                Handler = parameters => actionHandler(parameters),
                Metadata = new NamedValues(metadata ?? new Dictionary<string, object>()),
                Synonyms = synonyms,
                IsHidden = isHidden
            };

            if (!isSynonym)
            {
                _actions.Add(action);
            }
        }

        protected internal class ActionExecutedEventArgs : EventArgs
        {
            public ActionExecutedEventArgs(IEnumerable<string> synonyms, NamedValues parameters)
            {
                Synonyms = synonyms;
                Parameters = parameters;
            }

            public IEnumerable<string> Synonyms { get; }

            public NamedValues Parameters { get; }
        }

        private class ActionDefinition
        {
            public Func<NamedValues, object> Handler { get; set; }

            public NamedValues Metadata { get; set; }

            public IList<string> Synonyms { get; set; }

            public bool IsHidden { get; set; }
        }

        private class DummyWorkflowStateRepository : IWorkflowStateRepository
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
            }

            public void MarkWorkflowAsSleeping(WorkflowBase workflow)
            {
            }

            public void MarkWorkflowAsInProgress(WorkflowBase workflow)
            {
            }
        }
    }
}
