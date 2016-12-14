using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WorkflowsCore.Time;
using static WorkflowsCore.StateMachines.StateExtensions;

namespace WorkflowsCore.StateMachines
{
    public class State<TState, THiddenState>
    {
        private readonly IList<AsyncOperation<TState, THiddenState>> _enterHandlers =
            new List<AsyncOperation<TState, THiddenState>>();

        private readonly IList<AsyncOperation<TState, THiddenState>> _activationHandlers =
            new List<AsyncOperation<TState, THiddenState>>();

        private readonly IList<AsyncOperation<TState, THiddenState>> _exitHandlers =
            new List<AsyncOperation<TState, THiddenState>>();

        private readonly IList<IAsyncOperationWrapper> _onAsyncHandlers = new List<IAsyncOperationWrapper>();
        private readonly IList<State<TState, THiddenState>> _children = new List<State<TState, THiddenState>>();

        private readonly List<string> _allowedActions = new List<string>();
        private readonly List<string> _disallowedActions = new List<string>();

        internal State(StateMachine<TState, THiddenState> stateMachine, StateId<TState, THiddenState> stateId)
        {
            StateMachine = stateMachine;
            StateId = stateId;
            Children = new ReadOnlyCollection<State<TState, THiddenState>>(_children);
        }

        private interface IAsyncOperationWrapper
        {
            Task WaitAndHandle(StateInstance instance);
        }

        public StateMachine<TState, THiddenState> StateMachine { get; }

        public StateId<TState, THiddenState> StateId { get; }

        public State<TState, THiddenState> Parent { get; private set; }

        public IReadOnlyCollection<State<TState, THiddenState>> Children { get; }

        public AsyncOperation<TState, THiddenState> OnEnter(string description = null)
        {
            var asyncOperation = new AsyncOperation<TState, THiddenState>(this, description);
            _enterHandlers.Add(asyncOperation);
            return asyncOperation;
        }

        public AsyncOperation<TState, THiddenState> OnActivate(string description = null)
        {
            var asyncOperation = new AsyncOperation<TState, THiddenState>(this, description);
            _activationHandlers.Add(asyncOperation);
            return asyncOperation;
        }

        public AsyncOperation<TState, THiddenState, TR> OnAsync<TR>(
            Func<Task<TR>> taskFactory,
            string description = null)
        {
            var asyncOperation = new AsyncOperation<TState, THiddenState, TR>(this, description);
            _onAsyncHandlers.Add(new AsyncOperationWrapper<TR>(taskFactory, asyncOperation));
            return asyncOperation;
        }

        public AsyncOperation<TState, THiddenState> OnAsync(Func<Task> taskFactory, string description = null)
        {
            var asyncOperation = new AsyncOperation<TState, THiddenState>(this, description);
            _onAsyncHandlers.Add(new AsyncOperationWrapper(taskFactory, asyncOperation));
            return asyncOperation;
        }

        public AsyncOperation<TState, THiddenState> OnExit(string description = null)
        {
            var asyncOperation = new AsyncOperation<TState, THiddenState>(this, description);
            _exitHandlers.Add(asyncOperation);
            return asyncOperation;
        }

        public State<TState, THiddenState> SubstateOf(StateId<TState, THiddenState> state)
        {
            return SubstateOf(
                !state.IsHiddenState
                    ? StateMachine.ConfigureState(state.Id)
                    : StateMachine.ConfigureHiddenState(state.HiddenId));
        }

        public State<TState, THiddenState> SubstateOf(State<TState, THiddenState> state)
        {
            state.AddChild(this);
            return this;
        }

        public State<TState, THiddenState> AllowActions(params string[] actions)
        {
            _allowedActions.AddRange(actions.Except(_allowedActions));
            return this;
        }

        public State<TState, THiddenState> DisallowActions(params string[] actions)
        {
            _disallowedActions.AddRange(actions.Except(_disallowedActions));
            return this;
        }

        public State<TState, THiddenState> Description(string description)
        {
            throw new NotImplementedException();
        }

        public StateInstance Run(StateTransition<TState, THiddenState> transition) => 
            new StateInstance(this, transition);

        private StateInstance Run(
            StateTransition<TState, THiddenState> transition,
            IList<State<TState, THiddenState>> initialChildrenStates) =>
                new StateInstance(this, transition, initialChildrenStates);

        private void AddChild(State<TState, THiddenState> state)
        {
            state.Parent = this;
            _children.Add(state);
        }

        public class StateInstance
        {
            private readonly Action<State<TState, THiddenState>> _onStateChangedHandler;
            private TaskCompletionSource<StateTransition<TState, THiddenState>> _stateTransitionTaskCompletionSource;

            internal StateInstance(State<TState, THiddenState> state, StateTransition<TState, THiddenState> transition)
            {
                if (transition.Path.First() != state)
                {
                    throw new ArgumentOutOfRangeException(nameof(transition));
                }

                State = state;
                _onStateChangedHandler = transition.OnStateChangedHandler;
                Task = Run(transition, transition.Path.Skip(1).ToList());
            }

            internal StateInstance(
                State<TState, THiddenState> state,
                StateTransition<TState, THiddenState> transition,
                IList<State<TState, THiddenState>> initialChildrenStates)
            {
                State = state;
                _onStateChangedHandler = transition.OnStateChangedHandler;
                Task = Run(transition, initialChildrenStates);
            }

            public State<TState, THiddenState> State { get; }

            public Task<StateTransition<TState, THiddenState>> Task { get; }

            public StateInstance Child { get; private set; }

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

            public void InitiateTransitionTo(State<TState, THiddenState> state)
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
                        new StateTransition<TState, THiddenState>(
                            state,
                            operation,
                            onStateChangedHandler: _onStateChangedHandler));
                }
            }

            private async Task<StateTransition<TState, THiddenState>> Run(
                StateTransition<TState, THiddenState> transition,
                IList<State<TState, THiddenState>> initialChildrenStates)
            {
                var handlers = !transition.IsRestoringState ? State._enterHandlers : State._activationHandlers;
                foreach (var enterHandler in handlers)
                {
                    var newState = await enterHandler.ExecuteAsync();
                    if (newState != null)
                    {
                        transition = new StateTransition<TState, THiddenState>(newState, transition);
                        initialChildrenStates = transition.FindPathFrom(State);
                        if (newState == State)
                        {
                            initialChildrenStates = new State<TState, THiddenState>[0];
                        }

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

                foreach (var enterHandler in State._exitHandlers)
                {
                    var newState = await enterHandler.ExecuteAsync();
                    if (newState != null)
                    {
                        transition = new StateTransition<TState, THiddenState>(newState, transition);
                    }
                }

                return transition;
            }

            private async Task<StateTransition<TState, THiddenState>> HandleStateTransitions(
                StateTransition<TState, THiddenState> transition,
                IList<State<TState, THiddenState>> initialChildrenStates)
            {
                do
                {
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
                            new TaskCompletionSource<StateTransition<TState, THiddenState>>();

                        transition.CompleteTransition();
                        var task = await System.Threading.Tasks.Task.WhenAny(
                            Workflow.WaitForDate(DateTime.MaxValue),
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

                    initialChildrenStates = transition.FindPathFrom(State);
                    if (isFromInner && transition.State == State)
                    {
                        initialChildrenStates = new State<TState, THiddenState>[0];
                    }
                }
                while (initialChildrenStates != null);

                return transition;
            }

            private Task ProcessOnAsyncs()
            {
                var onAsyncs = State._onAsyncHandlers.Select(h => (Func<Task>)(() => h.WaitAndHandle(this))).ToArray();
                return onAsyncs.Any() ? Workflow.WaitForAny(onAsyncs) : System.Threading.Tasks.Task.CompletedTask;
            }
        }

        private class AsyncOperationWrapper : IAsyncOperationWrapper
        {
            private readonly Func<Task> _taskFactory;
            private readonly AsyncOperation<TState, THiddenState> _operation;

            public AsyncOperationWrapper(Func<Task> taskFactory, AsyncOperation<TState, THiddenState> operation)
            {
                _taskFactory = taskFactory;
                _operation = operation;
            }

            // ReSharper disable once FunctionNeverReturns
            public async Task WaitAndHandle(StateInstance instance)
            {
                while (true)
                {
                    await _taskFactory();
                    using (await Workflow.WaitForReadyAndStartOperation())
                    {
                        var newState = await _operation.ExecuteAsync();
                        if (newState != null)
                        {
                            instance.InitiateTransitionTo(newState);
                        }
                    }

                    await Workflow.WaitForReady();
                }
            }
        }

        private class AsyncOperationWrapper<TR> : IAsyncOperationWrapper
        {
            private readonly Func<Task<TR>> _taskFactory;
            private readonly AsyncOperation<TState, THiddenState, TR> _operation;

            public AsyncOperationWrapper(Func<Task<TR>> taskFactory, AsyncOperation<TState, THiddenState, TR> operation)
            {
                _taskFactory = taskFactory;
                _operation = operation;
            }

            // ReSharper disable once FunctionNeverReturns
            public async Task WaitAndHandle(StateInstance instance)
            {
                while (true)
                {
                    var res = await _taskFactory();
                    using (await Workflow.WaitForReadyAndStartOperation())
                    {
                        var newState = await _operation.ExecuteAsync(res);
                        if (newState != null)
                        {
                            instance.InitiateTransitionTo(newState);
                        }
                    }

                    await Workflow.WaitForReady();
                }
            }
        }
    }
}
