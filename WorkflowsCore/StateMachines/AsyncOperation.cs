using System;
using System.Threading.Tasks;

namespace WorkflowsCore.StateMachines
{
    public class BaseAsyncOperation<TState, TData>
    {
        private AsyncOperationHandler _handler;

        public BaseAsyncOperation(State<TState> parent, string description)
        {
            Parent = parent;
            Description = description;
        }

        public State<TState> Parent { get; }

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

        public AsyncOperation<TState, TR> Middleware<TR>(Func<Task<TR>> taskFactory)
        {
            throw new NotImplementedException();
        }

        public State<TState> GoTo(TState state)
        {
            throw new NotImplementedException();
        }

        public State<TState> GoTo(State<TState> state)
        {
            Handler = new GoToHandler(state);
            return Parent;
        }

        public State<TState> Do(Action action)
        {
            return Do(
                () =>
                {
                    action();
                    return Task.CompletedTask;
                });
        }

        public State<TState> Do(Func<Task> taskFactory)
        {
            Handler = new DoHandler(taskFactory);
            return Parent;
        }

        protected abstract class AsyncOperationHandler
        {
            public abstract Task<State<TState>> ExecuteAsync();

            public virtual Task<State<TState>> ExecuteAsync(TData data) => ExecuteAsync();
        }

        private class DoHandler : AsyncOperationHandler
        {
            private readonly Func<Task> _taskFactory;

            public DoHandler(Func<Task> taskFactory)
            {
                _taskFactory = taskFactory;
            }

            public override async Task<State<TState>> ExecuteAsync()
            {
                await _taskFactory();
                return null;
            }
        }

        private class GoToHandler : AsyncOperationHandler
        {
            private readonly State<TState> _newState;

            public GoToHandler(State<TState> newState)
            {
                _newState = newState;
            }

            public override Task<State<TState>> ExecuteAsync() => Task.FromResult(_newState);
        }
    }

    public sealed class AsyncOperationVoidData
    {
    }

    public class AsyncOperation<TState> : BaseAsyncOperation<TState, AsyncOperationVoidData>
    {
        public AsyncOperation(State<TState> parent, string description) 
            : base(parent, description)
        {
        }

        public AsyncOperation<TState> Middleware(Func<Task> taskFactory)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState> If(Func<Task<bool>> taskFactory, string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            TState state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            State<TState> state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public Task<State<TState>> ExecuteAsync() => Handler.ExecuteAsync();
    }

    public class AsyncOperation<TState, TData> : BaseAsyncOperation<TState, TData>
    {
        public AsyncOperation(State<TState> parent, string description) 
            : base(parent, description)
        {
        }

        public AsyncOperation<TState, TR> Middleware<TR>(Func<TData, Task<TR>> taskFactory)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, TData> Middleware(Func<Task> taskFactory)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, TData> If(Func<TData, Task<bool>> taskFactory, string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, TData> If(Func<Task<bool>> taskFactory, string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, TData> IfThenGoTo(
            Func<TData, Task<bool>> taskFactory,
            TState state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, TData> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            TState state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, TData> IfThenGoTo(
            Func<TData, Task<bool>> taskFactory,
            State<TState> state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public AsyncOperation<TState, TData> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            State<TState> state,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public State<TState> Do(Action<TData> action)
        {
            return Do(
                data =>
                {
                    action(data);
                    return Task.CompletedTask;
                });
        }

        public State<TState> Do(Func<TData, Task> taskFactory)
        {
            Handler = new DoHandlerWithData(taskFactory);
            return Parent;
        }

        public Task<State<TState>> ExecuteAsync(TData data) => Handler.ExecuteAsync(data);

        private class DoHandlerWithData : AsyncOperationHandler
        {
            private readonly Func<TData, Task> _taskFactory;

            public DoHandlerWithData(Func<TData, Task> taskFactory)
            {
                _taskFactory = taskFactory;
            }

            public override Task<State<TState>> ExecuteAsync()
            {
                throw new NotImplementedException();
            }

            public override async Task<State<TState>> ExecuteAsync(TData data)
            {
                await _taskFactory(data);
                return null;
            }
        }
    }
}
