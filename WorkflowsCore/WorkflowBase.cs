using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;

namespace WorkflowsCore
{
    public abstract class WorkflowBase
    {
        private readonly Func<IWorkflowStateRepository> _workflowRepoFactory;

        private readonly Lazy<IWorkflowMetadata> _metadata;

        private readonly Operation _initializationOperation;

        private readonly TaskCompletionSource<bool> _idTaskCompletionSource =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _completedTaskCompletionSource =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly IList<string> _actions = new List<string>();
        private readonly IDictionary<string, ActionDefinition> _actionsDefinitions =
            new Dictionary<string, ActionDefinition>();

        private readonly ConcurrentExclusiveSchedulerPair _concurrentExclusiveSchedulerPair;

        private readonly ActivationDatesManager _activationDatesManager = new ActivationDatesManager();

        private object _id;
        private Exception _exception;
        private bool _wasStared;
        private bool _isCancellationRequested;
        private volatile Operation _operationInProgress;

        protected WorkflowBase()
            : this(null)
        {
        }

        protected WorkflowBase(Func<IWorkflowStateRepository> workflowRepoFactory)
        {
            _metadata = new Lazy<IWorkflowMetadata>(
                () => Utilities.WorkflowMetadataCache.GetWorkflowMetadata(GetType()),
                LazyThreadSafetyMode.PublicationOnly);
            _concurrentExclusiveSchedulerPair = Utilities.WorkflowsTaskScheduler == null
                ? new ConcurrentExclusiveSchedulerPair()
                : new ConcurrentExclusiveSchedulerPair(Utilities.WorkflowsTaskScheduler);
            _workflowRepoFactory = workflowRepoFactory;
            if (_workflowRepoFactory == null)
            {
                _idTaskCompletionSource.SetResult(true);
            }

            _initializationOperation = new Operation(this);
            _initializationOperation.StartOperation();

            CancellationTokenSource = new CancellationTokenSource();
            CancellationToken = CancellationTokenSource.Token;
            _activationDatesManager.NextActivationDateChanged += OnNextActivationDateChanged;
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

                _idTaskCompletionSource.TrySetResult(true);
            }
        }

        public IWorkflowMetadata Metadata => _metadata.Value;

        public Task StartedTask => _initializationOperation.Task;

        public Task ReadyTask => OperationInProgress?.Task ?? Task.CompletedTask;

        public Task CompletedTask => _completedTaskCompletionSource.Task;

        public bool IsWorkflowTaskScheduler => TaskScheduler.Current == WorkflowTaskScheduler;

        internal NamedValues TransientData { get; } = new NamedValues();

        protected static ISystemClock SystemClock => Utilities.SystemClock;

        [DataField(IsTransient = true)]
        protected object ActionResult { get; set; }

        private CancellationTokenSource CancellationTokenSource { get; }

        private CancellationToken CancellationToken { get; }

        private TaskScheduler WorkflowTaskScheduler => _concurrentExclusiveSchedulerPair.ExclusiveScheduler;

        private AsyncLocal<Operation> CurrentOperation { get; } = new AsyncLocal<Operation>();

        private Operation OperationInProgress
        {
            get { return _operationInProgress; }
#pragma warning disable 420
            set { Interlocked.Exchange(ref _operationInProgress, value); }
#pragma warning restore 420
        }

        [DataField(IsTransient = true)]
        private DateTimeOffset? NextActivationDate => _activationDatesManager.NextActivationDate;

        [DataField]
        private IDictionary<string, int> ActionStats { get; set; }

        public void StartWorkflow(
            object id = null,
            IReadOnlyDictionary<string, object> initialWorkflowData = null,
            IReadOnlyDictionary<string, object> loadedWorkflowData = null,
            IReadOnlyDictionary<string, object> initialWorkflowTransientData = null,
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
                    w => Run(
                        id,
                        initialWorkflowData,
                        loadedWorkflowData,
                        initialWorkflowTransientData,
                        beforeWorkflowStarted),
                    forceExecution: true).Unwrap().ContinueWith(
                        t => ProcessWorkflowCompletion(t, afterWorkflowFinished),
                        CancellationToken.None,
                        TaskContinuationOptions.RunContinuationsAsynchronously | TaskContinuationOptions.PreferFairness,
                        WorkflowTaskScheduler);
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
                CancellationToken,
                () =>
                {
                    try
                    {
                        return Task.Factory.StartNew(
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
                            WorkflowTaskScheduler);
                    }
                    catch (ObjectDisposedException)
                    {
                        throw new TaskCanceledException("CancellationTokenSource has been disposed");
                    }
                }).Unwrap();
        }

        public Task<T> DoWorkflowTaskAsync<T>(Func<WorkflowBase, T> func, bool forceExecution = false)
        {
            return Utilities.SetCurrentCancellationTokenTemporarily(
                CancellationToken,
                () =>
                {
                    try
                    {
                        return Task.Factory.StartNew(
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
                            WorkflowTaskScheduler);
                    }
                    catch (ObjectDisposedException)
                    {
                        throw new TaskCanceledException("CancellationTokenSource has been disposed");
                    }
                }).Unwrap();
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
                    using (var operation = await this.WaitForReadyAndStartOperation())
                    {
                        ActionResult = default(T);
                        var res = ExecuteActionCore(action, parameters, throwNotAllowed);
                        await operation.WaitForAllInnerOperationsCompletion();
                        SaveWorkflowData();
                        return res.Any() ? res.Cast<T>().Single() : (T)ActionResult;
                    }
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

        public Task<IList<string>> GetAvailableActionsAsync(IReadOnlyDictionary<string, object> parameters = null)
        {
            var namedValues = GetNamedValues(parameters);
            return DoWorkflowTaskAsync(
                async () =>
                {
                    using (await this.WaitForReadyAndStartOperation())
                    {
                        return GetAvailableActions(namedValues);
                    }
                }).Unwrap();
        }

        public Task<T> TryGetDataFieldAsync<T>(string key, bool forceExecution = false) =>
            RunViaWorkflowTaskScheduler(() => Metadata.TryGetDataField<T>(this, key), forceExecution);

        public Task<T> GetDataFieldAsync<T>(string key, bool forceExecution = false) =>
            RunViaWorkflowTaskScheduler(() => Metadata.GetDataField<T>(this, key), forceExecution);

        public Task<T> GetTransientDataFieldAsync<T>(string key, bool forceExecution = false) =>
            RunViaWorkflowTaskScheduler(() => Metadata.GetTransientDataField<T>(this, key), forceExecution);

        internal static Exception GetAggregatedExceptions(Exception exception, Exception newException) =>
            exception == null ? newException : new AggregateException(exception, newException);

        internal Task ClearTimesExecutedAsync(string action)
        {
            return DoWorkflowTaskAsync(
                () =>
                {
                    ClearTimesExecuted(action);
                    SaveWorkflowData();
                });
        }

        internal void AddActivationDate(CancellationToken token, DateTimeOffset date) =>
            _activationDatesManager.AddActivationDate(token, date);

        internal void OnCancellationTokenCanceled(CancellationToken token) =>
            _activationDatesManager.OnCancellationTokenCanceled(token);

        internal Task WaitForOperationOrInnerOperationCompletion()
        {
            return CurrentOperation.Value.Parent == null
                ? ReadyTask
                : CurrentOperation.Value.Parent.WaitForInnerOperationCompletion();
        }

        internal void CancelOperation()
        {
            EnsureOperationExists();
            CurrentOperation.Value.Parent?.OnInnerOperationCanceled();
        }

        protected internal static Task WaitForAllInnerOperationsCompletion(IDisposable operation) =>
            ((Operation)operation).WaitForAllInnerOperationsCompletion();

        /// <summary>
        /// This function should be thread-safe
        /// </summary>
        protected internal void CreateOperation(
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = 0)
        {
            var innerOperation = CurrentOperation.Value?.TryCreateInnerOperation(filePath, lineNumber);
            /* ReSharper disable ExplicitCallerInfoArgument */
            CurrentOperation.Value = innerOperation ?? new Operation(this, filePath, lineNumber);
            /* ReSharper restore ExplicitCallerInfoArgument */
        }

        /// <summary>
        /// This function is not thread-safe and should be called within workflow task scheduler only
        /// </summary>
        protected internal IDisposable TryStartOperation()
        {
            EnsureOperationExists();

            if (ReadyTask.IsCanceled)
            {
                return null;
            }

            if (ReadyTask.IsCompleted)
            {
                return CurrentOperation.Value.StartOperation();
            }

            return CurrentOperation.Value.Parent?.TryStartInnerOperation(CurrentOperation.Value);
        }

        protected internal void ResetOperation() => CurrentOperation.Value = null;

        protected internal void ResetOperationToParent() => CurrentOperation.Value = CurrentOperation.Value?.Parent;

        protected internal void ImportOperation(IDisposable operation) => CurrentOperation.Value = (Operation)operation;

        protected internal bool WasExecuted(string action) => TimesExecuted(action) > 0;

        protected internal virtual bool IsActionAllowed(string action, NamedValues parameters) => true;

        protected internal IList<string> GetActionSynonyms(string action) => GetActionDefinition(action).Synonyms;

        protected NamedValues GetActionMetadata(string action) => GetActionDefinition(action).Metadata;

        protected void SaveWorkflowData() =>
            CreateWorkflowRepositoryAndDoAction(r => r.SaveWorkflowData(this, NextActivationDate));

        protected virtual void OnInit()
        {
            ActionStats = new Dictionary<string, int>();
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

        protected virtual void OnCanceled(Exception exception)
        {
        }

        protected virtual void OnFaulted(Exception exception)
        {
        }

        protected int TimesExecuted(string action)
        {
            var actionDefinition = GetActionDefinition(action);
            int stats;
            return !ActionStats.TryGetValue(actionDefinition.Synonyms.First(), out stats) ? 0 : stats;
        }

        protected void ClearTimesExecuted(string action) =>
            ActionStats.Remove(GetActionDefinition(action).Synonyms.First());

        protected void ConfigureAction(
            string action,
            Action actionHandler = null,
            IReadOnlyDictionary<string, object> metadata = null,
            string synonym = null,
            IEnumerable<string> synonyms = null,
            bool isHidden = false,
            Func<NamedValues, bool> isActionAllowed = null)
        {
            ConfigureActionCore(
                action,
                _ =>
                {
                    actionHandler?.Invoke();
                    return new object[0];
                },
                metadata,
                synonym,
                synonyms,
                isHidden,
                isActionAllowed);
        }

        protected void ConfigureAction(
            string action,
            Action<NamedValues> actionHandler,
            IReadOnlyDictionary<string, object> metadata = null,
            string synonym = null,
            IEnumerable<string> synonyms = null,
            bool isHidden = false,
            Func<NamedValues, bool> isActionAllowed = null)
        {
            ConfigureActionCore(
                action,
                parameters =>
                {
                    actionHandler(parameters);
                    return new object[0];
                },
                metadata,
                synonym,
                synonyms,
                isHidden,
                isActionAllowed);
        }

        protected void ConfigureAction<T>(
            string action,
            Func<T> actionHandler,
            IReadOnlyDictionary<string, object> metadata = null,
            string synonym = null,
            IEnumerable<string> synonyms = null,
            bool isHidden = false,
            Func<NamedValues, bool> isActionAllowed = null)
        {
            ConfigureAction(action, _ => actionHandler(), metadata, synonym, synonyms, isHidden, isActionAllowed);
        }

        protected void ConfigureAction<T>(
            string action,
            Func<NamedValues, T> actionHandler,
            IReadOnlyDictionary<string, object> metadata = null,
            string synonym = null,
            IEnumerable<string> synonyms = null,
            bool isHidden = false,
            Func<NamedValues, bool> isActionAllowed = null)
        {
            ConfigureActionCore(
                action,
                parameters => new object[] { actionHandler(parameters) },
                metadata,
                synonym,
                synonyms,
                isHidden,
                isActionAllowed);
        }

        protected void ExecuteAction(string action, bool throwNotAllowed = true) =>
            ExecuteAction<object>(action, new Dictionary<string, object>(), throwNotAllowed);

        protected T ExecuteAction<T>(string action, bool throwNotAllowed = true) =>
            ExecuteAction<T>(action, new Dictionary<string, object>(), throwNotAllowed);

        protected T ExecuteAction<T>(
            string action,
            IReadOnlyDictionary<string, object> parameters,
            bool throwNotAllowed = true)
        {
            return ExecuteActionCore(action, parameters, throwNotAllowed).Cast<T>().SingleOrDefault();
        }

        protected bool IsActionHidden(string action) => _actionsDefinitions[action].IsHidden;

        private static NamedValues GetNamedValues(IReadOnlyDictionary<string, object> src) =>
            src == null ? new NamedValues() : new NamedValues(src);

        private static bool IsActionAllowed(NamedValues parameters) => true;

        private Task Run(
            object id,
            IReadOnlyDictionary<string, object> initialWorkflowData,
            IReadOnlyDictionary<string, object> loadedWorkflowData,
            IReadOnlyDictionary<string, object> initialWorkflowTransientData,
            Action beforeWorkflowStarted)
        {
            OnInit();
            if (initialWorkflowData != null)
            {
                Metadata.SetData(this, initialWorkflowData);
            }

            if (initialWorkflowTransientData != null)
            {
                Metadata.SetTransientData(this, initialWorkflowTransientData);
            }

            if (id != null)
            {
                Id = id;
            }

            if (loadedWorkflowData == null)
            {
                OnCreated();
                SaveWorkflowData();
            }
            else
            {
                Metadata.SetData(this, loadedWorkflowData);
                OnLoaded();
            }

            return _idTaskCompletionSource.Task.ContinueWith(
                _ =>
                {
                    beforeWorkflowStarted?.Invoke();

                    ImportOperation(_initializationOperation);
                    var task = RunAsync();
                    _initializationOperation.Dispose();
                    return task;
                },
                WorkflowTaskScheduler).Unwrap();
        }

        private void ProcessWorkflowCompletion(Task task, Action afterWorkflowFinished)
        {
            lock (CancellationTokenSource)
            {
                var canceled = false;
                Exception exception = null;
                try
                {
                    ProcessWorkflowCompletionCore(task, afterWorkflowFinished, out exception, out canceled);
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

                    CancellationTokenSource.Dispose();
                }
            }
        }

        private void ProcessWorkflowCompletionCore(
            Task task,
            Action afterWorkflowFinished,
            out Exception exception,
            out bool canceled)
        {
            canceled = false;
            exception = null;

            // Ensure any background child activities are canceled and ignore further workflow actions if not enforced
            var isCancellationRequested = _isCancellationRequested;
            Cancel();
            try
            {
                switch (task.Status)
                {
                    case TaskStatus.RanToCompletion:
                        if (isCancellationRequested || _exception != null)
                        {
                            HandleCancellation(isCancellationRequested, out exception, out canceled);
                        }
                        else
                        {
                            CreateWorkflowRepositoryAndDoAction(r => r.MarkWorkflowAsCompleted(this));
                        }

                        break;
                    case TaskStatus.Faulted:
                        // ReSharper disable once PossibleNullReferenceException
                        _exception = GetAggregatedExceptions(_exception, task.Exception.GetBaseException());
                        HandleCancellation(isCancellationRequested, out exception, out canceled);
                        break;
                    case TaskStatus.Canceled:
                        if (isCancellationRequested || _exception != null)
                        {
                            HandleCancellation(isCancellationRequested, out exception, out canceled);
                        }
                        else
                        {
                            exception = new TaskCanceledException("Unexpected cancellation of child activity");
                            var exceptionCopy = exception;
                            CreateWorkflowRepositoryAndDoAction(r => r.MarkWorkflowAsFailed(this, exceptionCopy));
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
                afterWorkflowFinished?.Invoke();
            }
        }

        private void HandleCancellation(bool isCancellationRequested, out Exception exception, out bool canceled)
        {
            exception = null;
            canceled = false;

            if (!isCancellationRequested && _exception != null)
            {
                CreateWorkflowRepositoryAndDoAction(r => r.MarkWorkflowAsFailed(this, _exception));
                exception = _exception;
                OnFaulted(_exception);
                return;
            }

            CreateWorkflowRepositoryAndDoAction(r => r.MarkWorkflowAsCanceled(this, _exception));
            canceled = true;
            OnCanceled(_exception);
        }

        private void Cancel(Exception exception = null)
        {
            lock (CancellationTokenSource)
            {
                _isCancellationRequested |= exception == null;

                if (!CancellationTokenSource.IsCancellationRequested)
                {
                    RunViaWorkflowTaskScheduler(
                        () =>
                        {
                            _idTaskCompletionSource.TrySetResult(false);
                            _initializationOperation.TryCancel();
                            OperationInProgress?.TryCancel();
                            OperationInProgress = Operation.Canceled;
                            try
                            {
                                CancellationTokenSource.Cancel();
                            }
                            catch (Exception ex)
                            {
                                _exception = GetAggregatedExceptions(_exception, ex);
                            }
                        },
                        forceExecution: exception == null); // If workflow is not started it cannot be stopped with exception but it can be canceled
                }

                if (exception != null)
                {
                    try
                    {
                        // ReSharper disable once UnusedVariable
                        var token = CancellationTokenSource.Token;
                    }
                    catch (ObjectDisposedException)
                    {
                        throw new InvalidOperationException("Workflow has been terminated", exception);
                    }

                    _exception = GetAggregatedExceptions(_exception, exception);
                }
            }
        }

        private object[] ExecuteActionCore(
            string action,
            IReadOnlyDictionary<string, object> parameters,
            bool throwNotAllowed)
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

            return result;
        }

        private object[] OnExecuteAction(
            string action,
            NamedValues parameters,
            bool throwNotAllowed,
            out bool wasExecuted)
        {
            var actionDefinition = GetActionDefinition(action);

            if (!IsActionAllowedCore(actionDefinition, parameters))
            {
                if (throwNotAllowed)
                {
                    throw new InvalidOperationException($"{action} action is not allowed");
                }

                wasExecuted = false;
                return new object[0];
            }

            wasExecuted = true;
            var primaryName = actionDefinition.Synonyms.First();
            parameters.SetDataField("Action", primaryName);
            Metadata.SetDataOrTransientData(this, parameters.Data);
            var result = actionDefinition.Handler(parameters);

            int stats;
            ActionStats.TryGetValue(primaryName, out stats);
            ActionStats[primaryName] = ++stats;

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

        private bool IsActionAllowedCore(ActionDefinition actionDefinition, NamedValues parameters) =>
            actionDefinition.IsActionAllowed(parameters) && IsActionAllowed(actionDefinition.Synonyms.First(), parameters);

        private IList<string> GetAvailableActions(NamedValues parameters)
        {
            return _actions.Where(a =>
            {
                var actionDefinition = GetActionDefinition(a);
                return IsActionAllowedCore(actionDefinition, parameters) && !actionDefinition.IsHidden;
            }).ToList();
        }

        private void ConfigureActionCore(
            string action,
            Func<NamedValues, object[]> actionHandler,
            IReadOnlyDictionary<string, object> metadata,
            string synonym,
            IEnumerable<string> synonyms,
            bool isHidden,
            Func<NamedValues, bool> isActionAllowed)
        {
            isActionAllowed = isActionAllowed ?? IsActionAllowed;
            var allSynonyms = new List<string> { action };
            if (synonym != null)
            {
                allSynonyms.Add(synonym);
            }

            allSynonyms.AddRange(synonyms ?? new List<string>());
            ConfigureActionCoreHelper(
                action,
                actionHandler,
                metadata,
                isActionAllowed,
                allSynonyms,
                isHidden: isHidden);

            foreach (var curSynonym in allSynonyms.Skip(1))
            {
                ConfigureActionCoreHelper(
                    curSynonym,
                    actionHandler,
                    metadata,
                    isActionAllowed,
                    allSynonyms,
                    true,
                    isHidden);
            }
        }

        private void ConfigureActionCoreHelper(
            string action,
            Func<NamedValues, object[]> actionHandler,
            IReadOnlyDictionary<string, object> metadata,
            Func<NamedValues, bool> isActionAllowed,
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
                Handler = actionHandler,
                Metadata = GetNamedValues(metadata),
                Synonyms = synonyms,
                IsHidden = isHidden,
                IsActionAllowed = isActionAllowed
            };

            if (!isSynonym)
            {
                _actions.Add(action);
            }
        }

        private void OnNextActivationDateChanged(object sender, EventArgs args)
        {
            if (CancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            SaveWorkflowData();
        }

        private void EnsureOperationExists()
        {
            if (CurrentOperation.Value == null)
            {
                throw new InvalidOperationException("Operation should be created first");
            }
        }

        private void CreateWorkflowRepositoryAndDoAction(Action<IWorkflowStateRepository> action)
        {
            if (_workflowRepoFactory == null)
            {
                return;
            }

            var repo = _workflowRepoFactory();
            using (repo as IDisposable)
            {
                action(repo);
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
            public Func<NamedValues, object[]> Handler { get; set; }

            public NamedValues Metadata { get; set; }

            public IList<string> Synonyms { get; set; }

            public bool IsHidden { get; set; }

            public Func<NamedValues, bool> IsActionAllowed { get; set; }
        }

        private sealed class Operation : IDisposable
        {
            public static readonly Operation Canceled = new Operation();

            private readonly WorkflowBase _workflow;
            private readonly string _filePath;
            private readonly int _lineNumber;

            private readonly TaskCompletionSource<bool> _tcs =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            private TaskCompletionSource<bool> _tcsInner;

            private int _counter = 1;
            private Operation _innerOperationInProgress;

            public Operation(
                WorkflowBase workflow,
                [CallerFilePath] string filePath = null,
                [CallerLineNumber] int lineNumber = 0)
            {
                _workflow = workflow;
                _filePath = filePath;
                _lineNumber = lineNumber;
            }

            private Operation() => TryCancel();

            private Operation(Operation parent, string filePath, int lineNumber)
            {
                _workflow = parent._workflow;
                Parent = parent;
                _filePath = filePath;
                _lineNumber = lineNumber;
            }

            public Task Task => _tcs.Task;

            public Operation Parent { get; }

            public void Dispose()
            {
                lock (_tcs)
                {
                    --_counter;
                    if (_counter == 1)
                    {
                        _tcsInner.TrySetResult(true);
                    }
                    else if (_counter <= 0)
                    {
                        _tcs.TrySetResult(true);
                        Parent?.OnInnerOperationCompletion();
                    }
                }
            }

            public Operation TryCreateInnerOperation(string filePath, int lineNumber)
            {
                lock (_tcs)
                {
                    if (_counter <= 0)
                    {
                        return Parent?.TryCreateInnerOperation(filePath, lineNumber);
                    }

                    ++_counter;
                    if (_tcsInner?.Task.IsCompleted ?? true)
                    {
                        _tcsInner = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }

                    return new Operation(this, filePath, lineNumber);
                }
            }

            public Operation StartOperation() => _workflow.OperationInProgress = this;

            public Operation TryStartInnerOperation(Operation innerOperation)
            {
                if (_innerOperationInProgress != null)
                {
                    return null;
                }

                _innerOperationInProgress = innerOperation;
                return _innerOperationInProgress;
            }

            public void TryCancel()
            {
                _tcs.TrySetCanceled();
                _tcsInner?.TrySetCanceled();
                Parent?.TryCancel();
            }

            public Task WaitForInnerOperationCompletion() => _innerOperationInProgress?.Task ?? Task.CompletedTask;

            public Task WaitForAllInnerOperationsCompletion() => _tcsInner?.Task ?? Task.CompletedTask;

            public void OnInnerOperationCanceled() => Dispose();

            public override string ToString() => $"Operation from {ToStringCore()}";

            private string ToStringCore() =>
                $"{(Parent == null ? string.Empty : Parent.ToStringCore() + " -> ")}{Path.GetFileName(_filePath)}:{_lineNumber}";

            private void OnInnerOperationCompletion()
            {
                Interlocked.Exchange(ref _innerOperationInProgress, null);
                Dispose();
            }
        }
    }
}
