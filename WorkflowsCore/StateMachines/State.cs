using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WorkflowsCore.Time;
using static WorkflowsCore.StateMachines.StateExtensions;

namespace WorkflowsCore.StateMachines
{
    public class State<T>
    {
        private readonly IList<AsyncOperation<T>> _enterHandlers = new List<AsyncOperation<T>>();
        private readonly IList<AsyncOperation<T>> _exitHandlers = new List<AsyncOperation<T>>();
        private readonly IList<IAsyncOperationWrapper> _onAsyncHandlers = new List<IAsyncOperationWrapper>();
        private readonly IList<State<T>> _children = new List<State<T>>();

        public State(T state) 
            : this()
        {
        }

        public State(string hiddenStateName) 
            : this()
        {
        }

        private State()
        {
            Children = new ReadOnlyCollection<State<T>>(_children);
        }

        private interface IAsyncOperationWrapper
        {
            Task WaitAndHandle(StateInstance instance);
        }

        public State<T> Parent { get; private set; }

        public IReadOnlyCollection<State<T>> Children { get; }

        public int Depth { get; private set; }

        public AsyncOperation<T> OnEnter(string description = null)
        {
            var asyncOperation = new AsyncOperation<T>(this, description);
            _enterHandlers.Add(asyncOperation);
            return asyncOperation;
        }

        public AsyncOperation<T> OnActivate(string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<T, TR> OnAsync<TR>(Func<Task<TR>> taskFactory, string description = null)
        {
            var asyncOperation = new AsyncOperation<T, TR>(this, description);
            _onAsyncHandlers.Add(new AsyncOperationWrapper<TR>(taskFactory, asyncOperation));
            return asyncOperation;
        }

        public AsyncOperation<T> OnAsync(Func<Task> taskFactory, string description = null)
        {
            var asyncOperation = new AsyncOperation<T>(this, description);
            _onAsyncHandlers.Add(new AsyncOperationWrapper(taskFactory, asyncOperation));
            return asyncOperation;
        }

        public AsyncOperation<T> OnExit(string description = null)
        {
            var asyncOperation = new AsyncOperation<T>(this, description);
            _exitHandlers.Add(asyncOperation);
            return asyncOperation;
        }

        public State<T> SubstateOf(T state)
        {
            throw new NotImplementedException();
        }

        public State<T> SubstateOf(State<T> state)
        {
            state.AddChild(this);
            return this;
        }

        public State<T> AllowActions(params string[] actions)
        {
            throw new NotImplementedException();
        }

        public State<T> DisallowActions(params string[] actions)
        {
            throw new NotImplementedException();
        }

        public State<T> Description(string description)
        {
            throw new NotImplementedException();
        }

        public StateInstance Run(StateTransition<T> transition, IList<State<T>> initialChildrenStates) => 
            new StateInstance(this, transition, initialChildrenStates);

        private void AddChild(State<T> state)
        {
            state.Parent = this;
            _children.Add(state);
        }

        public class StateInstance
        {
            public StateInstance(State<T> state, StateTransition<T> transition, IList<State<T>> initialChildrenStates)
            {
                State = state;
                Task = Run(transition, initialChildrenStates);
            }

            public State<T> State { get; }

            public Task<IList<State<T>>> Task { get; }

            public StateInstance Child { get; private set; }

            private TaskCompletionSource<StateTransition<T>> StateTransitionTaskCompletionSource { get; set; } =
                new TaskCompletionSource<StateTransition<T>>();

            public void InitiateTransitionTo(State<T> state)
            {
                if (Child != null)
                {
                    Child.InitiateTransitionTo(state);
                }
                else
                {
                    Workflow.CreateOperation();
                    var operation = Workflow.TryStartOperation();
                    StateTransitionTaskCompletionSource.SetResult(new StateTransition<T>(state, operation));
                }
            }

            private async Task<IList<State<T>>> Run(
                StateTransition<T> transition,
                IList<State<T>> initialChildrenStates)
            {
                foreach (var enterHandler in State._enterHandlers)
                {
                    var newState = await enterHandler.ExecuteAsync();
                }

                await Workflow.WaitForAny(
                    () => HandleStateTransitions(transition, initialChildrenStates),
                    () => Workflow.Optional(ProcessOnAsyncs()));

                foreach (var enterHandler in State._exitHandlers)
                {
                    var newState = await enterHandler.ExecuteAsync();
                }

                return new List<State<T>>();
            }

            private async Task HandleStateTransitions(
                StateTransition<T> transition,
                IList<State<T>> initialChildrenStates)
            {
                if (initialChildrenStates.Any())
                {
                    Child = initialChildrenStates.First().Run(transition, initialChildrenStates.Skip(1).ToList());
                    await Child.Task;
                }
                else
                {
                    transition.WorkflowOperation.Dispose();
                    var task = await System.Threading.Tasks.Task.WhenAny(
                        Workflow.WaitForDate(DateTime.MaxValue),
                        StateTransitionTaskCompletionSource.Task);

                    if (task == StateTransitionTaskCompletionSource.Task)
                    {
                        HandleStateTransition(StateTransitionTaskCompletionSource.Task.Result);
                    }
                    else
                    {
                        StateTransitionTaskCompletionSource.SetCanceled();
                        await task;
                    }
                }
            }

            private Task ProcessOnAsyncs()
            {
                var onAsyncs = State._onAsyncHandlers.Select(h => (Func<Task>)(() => h.WaitAndHandle(this))).ToArray();
                return onAsyncs.Any() ? Workflow.WaitForAny(onAsyncs) : System.Threading.Tasks.Task.CompletedTask;
            }

            private void HandleStateTransition(StateTransition<T> transition)
            {
                if (!State.HasChild(transition.State))
                {
                    return;
                }
            }
        }

        private class AsyncOperationWrapper : IAsyncOperationWrapper
        {
            private readonly Func<Task> _taskFactory;
            private readonly AsyncOperation<T> _operation;

            public AsyncOperationWrapper(Func<Task> taskFactory, AsyncOperation<T> operation)
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
            private readonly AsyncOperation<T, TR> _operation;

            public AsyncOperationWrapper(Func<Task<TR>> taskFactory, AsyncOperation<T, TR> operation)
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
