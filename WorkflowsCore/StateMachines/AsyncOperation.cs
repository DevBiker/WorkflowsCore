using System;
using System.Threading.Tasks;

namespace WorkflowsCore.StateMachines
{
    public class BaseAsyncOperation<TState, THiddenState, TData>
    {
        private AsyncOperationHandler _handler;

        public BaseAsyncOperation(State<TState, THiddenState> parent, string description)
        {
            Parent = parent;
            Description = description;
        }

        public State<TState, THiddenState> Parent { get; }

        public string Description { get; }

        protected AsyncOperationHandler Handler
        {
            get
            {
                return _handler;
            }

            set
            {
                if (_handler != null)
                {
                    throw new InvalidOperationException("Handler could be set only once");
                }

                _handler = value;
            }
        }

        public AsyncOperation<TState, THiddenState, TR> Middleware<TR>(Func<Task<TR>> taskFactory)
        {
            throw new NotImplementedException();
        }

        public State<TState, THiddenState> GoTo(StateId<TState, THiddenState> state)
        {
            throw new NotImplementedException();
        }

        public State<TState, THiddenState> GoTo(State<TState, THiddenState> state)
        {
            Handler = new GoToHandler(state);
            return Parent;
        }

        public State<TState, THiddenState> Do(Action action)
        {
            return Do(
                () =>
                {
                    action();
                    return Task.CompletedTask;
                });
        }

        public State<TState, THiddenState> Do(Func<Task> taskFactory)
        {
            Handler = new DoHandler(taskFactory);
            return Parent;
        }

        protected abstract class AsyncOperationHandler
        {
            public abstract Task<State<TState, THiddenState>> ExecuteAsync();

            public virtual Task<State<TState, THiddenState>> ExecuteAsync(TData data) => ExecuteAsync();
        }

        private class DoHandler : AsyncOperationHandler
        {
            private readonly Func<Task> _taskFactory;

            public DoHandler(Func<Task> taskFactory)
            {
                _taskFactory = taskFactory;
            }

            public override async Task<State<TState, THiddenState>> ExecuteAsync()
            {
                await _taskFactory();
                return null;
            }
        }

        private class GoToHandler : AsyncOperationHandler
        {
            private readonly State<TState, THiddenState> _newState;

            public GoToHandler(State<TState, THiddenState> newState)
            {
                _newState = newState;
            }

            public override Task<State<TState, THiddenState>> ExecuteAsync() => Task.FromResult(_newState);
        }
    }

    public sealed class AsyncOperationVoidData
    {
    }

    public class AsyncOperation<TState, THiddenState> : BaseAsyncOperation<TState, THiddenState, AsyncOperationVoidData>
    {
        public AsyncOperation(State<TState, THiddenState> parent, string description)
            : base(parent, description)
        {
        }

        public AsyncOperation<TState, THiddenState> Middleware(Func<Task> taskFactory)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, THiddenState> If(Func<Task<bool>> taskFactory, string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, THiddenState> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            StateId<TState, THiddenState> state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, THiddenState> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            State<TState, THiddenState> state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public Task<State<TState, THiddenState>> ExecuteAsync() => Handler.ExecuteAsync();
    }

    public class AsyncOperation<TState, THiddenState, TData> : BaseAsyncOperation<TState, THiddenState, TData>
    {
        public AsyncOperation(State<TState, THiddenState> parent, string description)
            : base(parent, description)
        {
        }

        public AsyncOperation<TState, THiddenState, TR> Middleware<TR>(Func<TData, Task<TR>> taskFactory)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, THiddenState, TData> Middleware(Func<Task> taskFactory)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, THiddenState, TData> If(
            Func<TData, Task<bool>> taskFactory,
            string description = null)
        {
            var handler = new IfHandlerWithData(Parent, taskFactory, description);
            Handler = handler;
            return handler.Child;
        }

        public AsyncOperation<TState, THiddenState, TData> If(Func<TData, bool> predicate, string description = null) =>
            If(d => Task.FromResult(predicate(d)), description);

        public AsyncOperation<TState, THiddenState, TData> If(Func<Task<bool>> taskFactory, string description = null) => 
            If(_ => taskFactory(), description);

        public AsyncOperation<TState, THiddenState, TData> If(Func<bool> predicate, string description = null) => 
            If(() => Task.FromResult(predicate()), description);

        public AsyncOperation<TState, THiddenState, TData> IfThenGoTo(
            Func<TData, Task<bool>> taskFactory,
            StateId<TState, THiddenState> state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, THiddenState, TData> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            StateId<TState, THiddenState> state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, THiddenState, TData> IfThenGoTo(
            Func<TData, Task<bool>> taskFactory,
            State<TState, THiddenState> state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, THiddenState, TData> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            State<TState, THiddenState> state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public State<TState, THiddenState> Do(Action<TData> action)
        {
            return Do(
                data =>
                {
                    action(data);
                    return Task.CompletedTask;
                });
        }

        public State<TState, THiddenState> Do(Func<TData, Task> taskFactory)
        {
            Handler = new DoHandlerWithData(taskFactory);
            return Parent;
        }

        public Task<State<TState, THiddenState>> ExecuteAsync(TData data) => Handler.ExecuteAsync(data);

        private class DoHandlerWithData : AsyncOperationHandler
        {
            private readonly Func<TData, Task> _taskFactory;

            public DoHandlerWithData(Func<TData, Task> taskFactory)
            {
                _taskFactory = taskFactory;
            }

            public override Task<State<TState, THiddenState>> ExecuteAsync()
            {
                throw new NotImplementedException();
            }

            public override async Task<State<TState, THiddenState>> ExecuteAsync(TData data)
            {
                await _taskFactory(data);
                return null;
            }
        }

        private class IfHandlerWithData : AsyncOperationHandler
        {
            private readonly Func<TData, Task<bool>> _taskFactory;

            public IfHandlerWithData(
                State<TState, THiddenState> parent,
                Func<TData, Task<bool>> taskFactory,
                string description)
            {
                _taskFactory = taskFactory;
                Child = new AsyncOperation<TState, THiddenState, TData>(parent, description);
            }

            public AsyncOperation<TState, THiddenState, TData> Child { get; }

            public override Task<State<TState, THiddenState>> ExecuteAsync()
            {
                throw new NotImplementedException();
            }

            public override async Task<State<TState, THiddenState>> ExecuteAsync(TData data)
            {
                var res = await _taskFactory(data);
                if (!res)
                {
                    return null;
                }

                return await Child.ExecuteAsync(data);
            }
        }
    }
}
