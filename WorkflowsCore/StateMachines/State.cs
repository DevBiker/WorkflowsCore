using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WorkflowsCore.Time;
using static WorkflowsCore.StateMachines.StateExtensions;

namespace WorkflowsCore.StateMachines
{
    public class State<TState, TInternalState>
    {
        private readonly IList<AsyncOperation<TState, TInternalState>> _enterHandlers =
            new List<AsyncOperation<TState, TInternalState>>();

        private readonly IList<AsyncOperation<TState, TInternalState>> _activationHandlers =
            new List<AsyncOperation<TState, TInternalState>>();

        private readonly IList<AsyncOperation<TState, TInternalState>> _exitHandlers =
            new List<AsyncOperation<TState, TInternalState>>();

        private readonly IList<IAsyncOperation<TState, TInternalState>> _onAsyncHandlers =
            new List<IAsyncOperation<TState, TInternalState>>();

        private readonly IList<State<TState, TInternalState>> _children = new List<State<TState, TInternalState>>();

        private readonly List<string> _allowedActions = new List<string>();
        private readonly List<string> _disallowedActions = new List<string>();
        private bool? _isHidden;

        internal State(StateMachine<TState, TInternalState> stateMachine, StateId<TState, TInternalState> stateId)
        {
            StateMachine = stateMachine;
            StateId = stateId;
            Children = new ReadOnlyCollection<State<TState, TInternalState>>(_children);
            EnterHandlers = new ReadOnlyCollection<AsyncOperation<TState, TInternalState>>(_enterHandlers);
            ActivationHandlers = new ReadOnlyCollection<AsyncOperation<TState, TInternalState>>(_activationHandlers);
            OnAsyncHandlers = new ReadOnlyCollection<IAsyncOperation<TState, TInternalState>>(_onAsyncHandlers);
            ExitHandlers = new ReadOnlyCollection<AsyncOperation<TState, TInternalState>>(_exitHandlers);
        }

        private interface IAsyncOperationWrapper : IAsyncOperation<TState, TInternalState>
        {
            Task WaitAndHandle(StateInstance instance);
        }

        public StateMachine<TState, TInternalState> StateMachine { get; }

        public StateId<TState, TInternalState> StateId { get; }

        public bool IsHidden => _isHidden ?? Parent?.IsHidden ?? false;

        public State<TState, TInternalState> Parent { get; private set; }

        public IReadOnlyCollection<State<TState, TInternalState>> Children { get; }

        public IReadOnlyCollection<AsyncOperation<TState, TInternalState>> EnterHandlers { get; }

        public IReadOnlyCollection<AsyncOperation<TState, TInternalState>> ActivationHandlers { get; }

        public IReadOnlyCollection<IAsyncOperation<TState, TInternalState>> OnAsyncHandlers { get; }

        public IReadOnlyCollection<AsyncOperation<TState, TInternalState>> ExitHandlers { get; }

        public string Description { get; private set; }

        public AsyncOperation<TState, TInternalState> OnEnter(string description = null, bool isHidden = false)
        {
            var asyncOperation = new AsyncOperation<TState, TInternalState>(this, description ?? "On Enter", isHidden);
            _enterHandlers.Add(asyncOperation);
            return asyncOperation;
        }

        public AsyncOperation<TState, TInternalState> OnActivate(string description = null, bool isHidden = false)
        {
            var asyncOperation = new AsyncOperation<TState, TInternalState>(this, description ?? "On Activate", isHidden);
            _activationHandlers.Add(asyncOperation);
            return asyncOperation;
        }

        public AsyncOperation<TState, TInternalState, TR> OnAsync<TR>(
            Func<Task<TR>> taskFactory,
            string description = null,
            Func<IDisposable> getOperationForImport = null,
            bool isHidden = false)
        {
            var asyncOperation = new AsyncOperation<TState, TInternalState, TR>(this, description, isHidden);
            _onAsyncHandlers.Add(new AsyncOperationWrapper<TR>(taskFactory, asyncOperation, getOperationForImport));
            return asyncOperation;
        }

        public AsyncOperation<TState, TInternalState> OnAsync(
            Func<Task> taskFactory,
            string description = null,
            Func<IDisposable> getOperationForImport = null,
            bool isHidden = false)
        {
            var asyncOperation = new AsyncOperation<TState, TInternalState>(this, description, isHidden);
            _onAsyncHandlers.Add(new AsyncOperationWrapper(taskFactory, asyncOperation, getOperationForImport));
            return asyncOperation;
        }

        public AsyncOperation<TState, TInternalState> OnExit(string description = null, bool isHidden = false)
        {
            var asyncOperation = new AsyncOperation<TState, TInternalState>(this, description ?? "On Exit", isHidden);
            _exitHandlers.Add(asyncOperation);
            return asyncOperation;
        }

        public State<TState, TInternalState> SubstateOf(StateId<TState, TInternalState> state) =>
            SubstateOf(StateMachine.ConfigureState(state));

        public State<TState, TInternalState> SubstateOf(State<TState, TInternalState> state)
        {
            state.AddChild(this);
            return this;
        }

        public State<TState, TInternalState> AllowActions(params string[] actions)
        {
            _allowedActions.AddRange(actions.Except(_allowedActions)); // TODO: Check action configured
            return this;
        }

        public State<TState, TInternalState> DisallowActions(params string[] actions)
        {
            _disallowedActions.AddRange(actions.Except(_disallowedActions)); // TODO: Check action configured
            return this;
        }

        public State<TState, TInternalState> Hide(bool isHidden = true)
        {
            _isHidden = isHidden ? true : (bool?)null;
            return this;
        }

        public State<TState, TInternalState> HasDescription(string description)
        {
            Description = description;
            return this;
        }

        public StateInstance Run(StateTransition<TState, TInternalState> transition) =>
            new StateInstance(this, transition);

        private StateInstance Run(
            StateTransition<TState, TInternalState> transition,
            IList<State<TState, TInternalState>> initialChildrenStates)
        {
            return new StateInstance(this, transition, initialChildrenStates);
        }

        private void AddChild(State<TState, TInternalState> state)
        {
            state.Parent = this;
            _children.Add(state);
        }

        public class StateInstance
        {
            private readonly Action<StateTransition<TState, TInternalState>> _onStateChangedHandler;
            private readonly Lazy<Task<StateTransition<TState, TInternalState>>> _taskLazy;
            private TaskCompletionSource<StateTransition<TState, TInternalState>> _stateTransitionTaskCompletionSource;
            private StateInstance _child;

            internal StateInstance(State<TState, TInternalState> state, StateTransition<TState, TInternalState> transition)
                : this(state, transition, transition.Path.Skip(1).ToList())
            {
                if (transition.Path.First() != state)
                {
                    throw new ArgumentOutOfRangeException(nameof(transition));
                }
            }

            internal StateInstance(
                State<TState, TInternalState> state,
                StateTransition<TState, TInternalState> transition,
                IList<State<TState, TInternalState>> initialChildrenStates)
            {
                State = state;
                _onStateChangedHandler = transition.OnStateChangedHandler;

                // We do not start state and inner states yet to avoid race condition in HandleStateTransitions() when
                // this transition is completed but parent Child is still not set
                _taskLazy = new Lazy<Task<StateTransition<TState, TInternalState>>>(
                    () => Run(transition, initialChildrenStates), false);
            }

            public State<TState, TInternalState> State { get; }

            public Task<StateTransition<TState, TInternalState>> Task => _taskLazy.Value;

            public StateInstance Parent { get; private set; }

            public StateInstance Child
            {
                get
                {
                    return _child;
                }

                private set
                {
                    if (_child != null)
                    {
                        _child.Parent = null;
                    }

                    _child = value;

                    if (_child != null)
                    {
                        _child.Parent = this;
                    }
                }
            }

            public bool? IsActionAllowed(string action)
            {
                var child = Child?.IsActionAllowed(action);
                if (child.HasValue)
                {
                    return child;
                }

                var actionSynonyms = Workflow.GetActionSynonyms(action);
                if (State._disallowedActions.Intersect(actionSynonyms).Any())
                {
                    return false;
                }

                if (State._allowedActions.Intersect(actionSynonyms).Any())
                {
                    return true;
                }

                return null;
            }

            public void InitiateTransitionTo(State<TState, TInternalState> state)
            {
                if (Child != null)
                {
                    Child.InitiateTransitionTo(state);
                }
                else
                {
                    Workflow.CreateOperation();
                    var operation = Workflow.TryStartOperation();
                    _stateTransitionTaskCompletionSource.SetResult(
                        new StateTransition<TState, TInternalState>(
                            state,
                            operation,
                            onStateChangedHandler: _onStateChangedHandler));
                }
            }

            public StateInstance GetNonInternalAncestor()
            {
                for (var parent = Parent; parent != null; parent = parent.Parent)
                {
                    if (!parent.State.StateId.IsInternalState)
                    {
                        return parent;
                    }
                }

                return null;
            }

            private async Task<StateTransition<TState, TInternalState>> Run(
                StateTransition<TState, TInternalState> transition,
                IList<State<TState, TInternalState>> initialChildrenStates)
            {
                var handlers = !transition.IsRestoringState ? State._enterHandlers : State._activationHandlers;
                foreach (var enterHandler in handlers)
                {
                    var newState = await enterHandler.ExecuteAsync();
                    if (newState != null)
                    {
                        transition = new StateTransition<TState, TInternalState>(newState, transition);
                        initialChildrenStates = newState == State
                            ? new State<TState, TInternalState>[0]
                            : transition.FindPathFrom(State);

                        if (initialChildrenStates == null)
                        {
                            return transition;
                        }
                    }
                }

                // ReSharper disable once AccessToModifiedClosure
                await Workflow.WaitForAny(
                    () => Workflow.Optional(ProcessOnAsyncs()),
                    async () => transition = await HandleStateTransitions(transition, initialChildrenStates));

                Workflow.ImportOperation(transition.WorkflowOperation);
                foreach (var enterHandler in State._exitHandlers)
                {
                    var newState = await enterHandler.ExecuteAsync();
                    if (newState != null)
                    {
                        transition = new StateTransition<TState, TInternalState>(newState, transition);
                    }
                }

                return transition;
            }

            private async Task<StateTransition<TState, TInternalState>> HandleStateTransitions(
                StateTransition<TState, TInternalState> transition,
                IList<State<TState, TInternalState>> initialChildrenStates)
            {
                do
                {
                    Workflow.ImportOperation(transition.WorkflowOperation);
                    var isFromInner = false;
                    if (initialChildrenStates.Any())
                    {
                        Child = initialChildrenStates.First().Run(transition, initialChildrenStates.Skip(1).ToList());
                        transition = await Child.Task;
                        isFromInner = true;
                        Child = null;
                    }
                    else
                    {
                        _stateTransitionTaskCompletionSource =
                            new TaskCompletionSource<StateTransition<TState, TInternalState>>();

                        transition.CompleteTransition(this);
                        var task = await System.Threading.Tasks.Task.WhenAny(
                            Workflow.WaitForDate(DateTimeOffset.MaxValue),
                            _stateTransitionTaskCompletionSource.Task);

                        if (task == _stateTransitionTaskCompletionSource.Task)
                        {
                            transition = _stateTransitionTaskCompletionSource.Task.Result;
                        }
                        else
                        {
                            _stateTransitionTaskCompletionSource.SetCanceled();
                            await task; // Exit via TaskCanceledException
                        }
                    }

                    initialChildrenStates = isFromInner && transition.State == State
                        ? new State<TState, TInternalState>[0]
                        : transition.FindPathFrom(State);
                }
                while (initialChildrenStates != null);

                return transition;
            }

            private Task ProcessOnAsyncs()
            {
                Workflow.ResetOperation();
                var onAsyncs = State._onAsyncHandlers
                    .Select(h => (Func<Task>)(() => ((IAsyncOperationWrapper)h).WaitAndHandle(this))).ToArray();
                return onAsyncs.Any() ? Workflow.WaitForAny(onAsyncs) : System.Threading.Tasks.Task.CompletedTask;
            }
        }

        private class AsyncOperationWrapper : IAsyncOperationWrapper
        {
            private readonly Func<Task> _taskFactory;
            private readonly AsyncOperation<TState, TInternalState> _operation;
            private readonly Func<IDisposable> _getOperationForImport;

            public AsyncOperationWrapper(
                Func<Task> taskFactory,
                AsyncOperation<TState, TInternalState> operation,
                Func<IDisposable> getOperationForImport)
            {
                _taskFactory = taskFactory;
                _operation = operation;
                _getOperationForImport = getOperationForImport;
            }

            public string Description => _operation.Description;

            public bool IsHidden => _operation.IsHidden;

            // ReSharper disable once FunctionNeverReturns
            public async Task WaitAndHandle(StateInstance instance)
            {
                var task = _taskFactory();
                while (true)
                {
                    await task;
                    var operationToImport = _getOperationForImport?.Invoke();
                    if (operationToImport != null)
                    {
                        Workflow.ImportOperation(operationToImport);
                    }

                    try
                    {
                        using (await Workflow.WaitForReadyAndStartOperation())
                        {
                            var newState = await _operation.ExecuteAsync();
                            if (newState != null)
                            {
                                instance.InitiateTransitionTo(newState);
                            }

                            Workflow.ResetOperation();
                            task = _taskFactory();
                        }
                    }
                    finally
                    {
                        operationToImport?.Dispose();
                    }
                }
            }

            public IList<TargetState<TState, TInternalState>> GetTargetStates(IEnumerable<string> conditions) =>
                _operation.GetTargetStates(conditions);
        }

        private class AsyncOperationWrapper<TR> : IAsyncOperationWrapper
        {
            private readonly Func<Task<TR>> _taskFactory;
            private readonly AsyncOperation<TState, TInternalState, TR> _operation;
            private readonly Func<IDisposable> _getOperationForImport;

            public AsyncOperationWrapper(
                Func<Task<TR>> taskFactory,
                AsyncOperation<TState, TInternalState, TR> operation,
                Func<IDisposable> getOperationForImport)
            {
                _taskFactory = taskFactory;
                _operation = operation;
                _getOperationForImport = getOperationForImport;
            }

            public string Description => _operation.Description;

            public bool IsHidden => _operation.IsHidden;

            // ReSharper disable once FunctionNeverReturns
            public async Task WaitAndHandle(StateInstance instance)
            {
                var task = _taskFactory();
                while (true)
                {
                    var res = await task;
                    var operationToImport = _getOperationForImport?.Invoke();
                    if (operationToImport != null)
                    {
                        Workflow.ImportOperation(operationToImport);
                    }

                    try
                    {
                        using (await Workflow.WaitForReadyAndStartOperation())
                        {
                            var newState = await _operation.ExecuteAsync(res);
                            if (newState != null)
                            {
                                instance.InitiateTransitionTo(newState);
                            }

                            Workflow.ResetOperation();
                            task = _taskFactory();
                        }
                    }
                    finally
                    {
                        operationToImport?.Dispose();
                    }
                }
            }

            public IList<TargetState<TState, TInternalState>> GetTargetStates(IEnumerable<string> conditions) =>
                _operation.GetTargetStates(conditions);
        }
    }
}
