using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WorkflowsCore.StateMachines
{
    public interface IAsyncOperation<TState, THiddenState>
    {
        string Description { get; }

        IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions);
    }

    public class TargetState<TState, THiddenState>
    {
        public TargetState(IEnumerable<string> conditions, State<TState, THiddenState> state)
        {
            Conditions = conditions.ToList();
            State = state;
        }

        public IEnumerable<string> Conditions { get; }

        public State<TState, THiddenState> State { get; }
    }

    public class BaseAsyncOperation<TState, THiddenState, TData> : IAsyncOperation<TState, THiddenState>
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

        public State<TState, THiddenState> GoTo(StateId<TState, THiddenState> state)
        {
            Handler = new GoToHandler(Parent.StateMachine.ConfigureState(state));
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

        public IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions) => 
            Handler.GetTargetStates(conditions);

        protected AsyncOperation<TState, THiddenState, TR> Invoke<TR>(Func<Task<TR>> taskFactory)
        {
            var handler = new InvokeHandler<TR>(Parent, taskFactory);
            Handler = handler;
            return handler.Child;
        }

        protected AsyncOperation<TState, THiddenState, TR> Invoke<TR>(Func<TR> func) =>
            Invoke(() => Task.FromResult(func()));

        protected abstract class AsyncOperationHandler
        {
            public abstract Task<State<TState, THiddenState>> ExecuteAsync();

            public virtual Task<State<TState, THiddenState>> ExecuteAsync(TData data) => ExecuteAsync();

            public abstract IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions);
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

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions) => 
                new List<TargetState<TState, THiddenState>>();
        }

        private class GoToHandler : AsyncOperationHandler
        {
            private readonly State<TState, THiddenState> _newState;

            public GoToHandler(State<TState, THiddenState> newState)
            {
                _newState = newState;
            }

            public override Task<State<TState, THiddenState>> ExecuteAsync() => Task.FromResult(_newState);

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions) =>
                new[] { new TargetState<TState, THiddenState>(conditions, _newState) };
        }

        private class InvokeHandler<TR> : AsyncOperationHandler
        {
            private readonly Func<Task<TR>> _taskFactory;

            public InvokeHandler(State<TState, THiddenState> parent, Func<Task<TR>> taskFactory)
            {
                _taskFactory = taskFactory;
                Child = new AsyncOperation<TState, THiddenState, TR>(parent);
            }

            public AsyncOperation<TState, THiddenState, TR> Child { get; }

            public override async Task<State<TState, THiddenState>> ExecuteAsync()
            {
                var res = await _taskFactory();

                return await Child.ExecuteAsync(res);
            }

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions) => 
                Child.GetTargetStates(conditions);
        }
    }

    public sealed class AsyncOperationVoidData
    {
    }

    public class AsyncOperation<TState, THiddenState> : BaseAsyncOperation<TState, THiddenState, AsyncOperationVoidData>
    {
        public AsyncOperation(State<TState, THiddenState> parent, string description = null)
            : base(parent, description)
        {
        }

        public AsyncOperation<TState, THiddenState> Invoke(Func<Task> taskFactory)
        {
            var handler = new InvokeHandler(Parent, taskFactory);
            Handler = handler;
            return handler.Child;
        }

        public AsyncOperation<TState, THiddenState> Invoke(Action action)
        {
            return Invoke(
                () =>
                {
                    action();
                    return Task.CompletedTask;
                });
        }

        public new AsyncOperation<TState, THiddenState, TR> Invoke<TR>(Func<Task<TR>> taskFactory) =>
            base.Invoke(taskFactory);

        public new AsyncOperation<TState, THiddenState, TR> Invoke<TR>(Func<TR> func) => base.Invoke(func);

        public AsyncOperation<TState, THiddenState> If(Func<Task<bool>> taskFactory, string description = null)
        {
            var handler = new IfHandler(Parent, taskFactory, description);
            Handler = handler;
            return handler.Child;
        }

        public AsyncOperation<TState, THiddenState> If(Func<bool> predicate, string description = null) =>
            If(() => Task.FromResult(predicate()), description);

        public AsyncOperation<TState, THiddenState> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            StateId<TState, THiddenState> state,
            string description = null)
        {
            var handler = new IfThenGoToHandler(
                Parent,
                taskFactory,
                Parent.StateMachine.ConfigureState(state),
                description);
            Handler = handler;
            return handler.Child;
        }

        public AsyncOperation<TState, THiddenState> IfThenGoTo(
            Func<bool> predicate,
            StateId<TState, THiddenState> state,
            string description = null)
        {
            return IfThenGoTo(() => Task.FromResult(predicate()), state, description);
        }

        public Task<State<TState, THiddenState>> ExecuteAsync() => Handler.ExecuteAsync();

        private class InvokeHandler : AsyncOperationHandler
        {
            private readonly Func<Task> _taskFactory;

            public InvokeHandler(State<TState, THiddenState> parent, Func<Task> taskFactory)
            {
                _taskFactory = taskFactory;
                Child = new AsyncOperation<TState, THiddenState>(parent);
            }

            public AsyncOperation<TState, THiddenState> Child { get; }

            public override async Task<State<TState, THiddenState>> ExecuteAsync()
            {
                await _taskFactory();

                return await Child.ExecuteAsync();
            }

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions) => 
                Child.GetTargetStates(conditions);
        }

        private class IfHandler : AsyncOperationHandler
        {
            private readonly Func<Task<bool>> _taskFactory;

            public IfHandler(
                State<TState, THiddenState> parent,
                Func<Task<bool>> taskFactory,
                string description)
            {
                _taskFactory = taskFactory;
                Child = new AsyncOperation<TState, THiddenState>(parent, description);
            }

            public AsyncOperation<TState, THiddenState> Child { get; }

            public override async Task<State<TState, THiddenState>> ExecuteAsync()
            {
                var res = await _taskFactory();
                if (!res)
                {
                    return null;
                }

                return await Child.ExecuteAsync();
            }

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions)
            {
                return Child.GetTargetStates(
                    Child.Description == null ? conditions : conditions.Concat(Enumerable.Repeat(Child.Description, 1)));
            }
        }

        private class IfThenGoToHandler : AsyncOperationHandler
        {
            private readonly Func<Task<bool>> _taskFactory;
            private readonly State<TState, THiddenState> _state;

            public IfThenGoToHandler(
                State<TState, THiddenState> parent,
                Func<Task<bool>> taskFactory,
                State<TState, THiddenState> state,
                string description)
            {
                _taskFactory = taskFactory;
                _state = state;
                Child = new AsyncOperation<TState, THiddenState>(parent, description);
            }

            public AsyncOperation<TState, THiddenState> Child { get; }

            public override async Task<State<TState, THiddenState>> ExecuteAsync()
            {
                var res = await _taskFactory();
                if (res)
                {
                    return _state;
                }

                return await Child.ExecuteAsync();
            }

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions)
            {
                conditions = conditions.ToList();
                var targetState = new TargetState<TState, THiddenState>(
                    Child.Description == null ? conditions : conditions.Concat(Enumerable.Repeat(Child.Description, 1)),
                    _state);
                return Enumerable.Repeat(targetState, 1).Concat(Child.GetTargetStates(conditions)).ToList();
            }
        }
    }

    public class AsyncOperation<TState, THiddenState, TData> : BaseAsyncOperation<TState, THiddenState, TData>
    {
        public AsyncOperation(State<TState, THiddenState> parent, string description = null)
            : base(parent, description)
        {
        }

        public AsyncOperation<TState, THiddenState, TR> Invoke<TR>(Func<TData, Task<TR>> taskFactory)
        {
            var handler = new InvokeHandlerWithData<TR>(Parent, taskFactory);
            Handler = handler;
            return handler.Child;
        }

        public AsyncOperation<TState, THiddenState, TR> Invoke<TR>(Func<TData, TR> func) =>
            Invoke(d => Task.FromResult(func(d)));

        public AsyncOperation<TState, THiddenState, TData> Invoke(Func<TData, Task> taskFactory)
        {
            var handler = new InvokeHandler(Parent, taskFactory);
            Handler = handler;
            return handler.Child;
        }

        public AsyncOperation<TState, THiddenState, TData> Invoke(Func<Task> taskFactory) => Invoke(_ => taskFactory());

        public AsyncOperation<TState, THiddenState, TData> Invoke(Action action)
        {
            return Invoke(
                () =>
                {
                    action();
                    return Task.CompletedTask;
                });
        }

        public AsyncOperation<TState, THiddenState, TData> Invoke(Action<TData> action)
        {
            return Invoke(
                d =>
                {
                    action(d);
                    return Task.CompletedTask;
                });
        }

        public new AsyncOperation<TState, THiddenState, TR> Invoke<TR>(Func<Task<TR>> taskFactory) =>
            base.Invoke(taskFactory);

        public new AsyncOperation<TState, THiddenState, TR> Invoke<TR>(Func<TR> func) => base.Invoke(func);

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

        public AsyncOperation<TState, THiddenState, TData> If(
            Func<Task<bool>> taskFactory,
            string description = null)
        {
            return If(_ => taskFactory(), description);
        }

        public AsyncOperation<TState, THiddenState, TData> If(Func<bool> predicate, string description = null) =>
            If(() => Task.FromResult(predicate()), description);

        public AsyncOperation<TState, THiddenState, TData> IfThenGoTo(
            Func<TData, Task<bool>> taskFactory,
            StateId<TState, THiddenState> state,
            string description = null)
        {
            var handler = new IfThenGoToHandlerWithData(
                Parent,
                taskFactory,
                Parent.StateMachine.ConfigureState(state),
                description);
            Handler = handler;
            return handler.Child;
        }

        public AsyncOperation<TState, THiddenState, TData> IfThenGoTo(
            Func<TData, bool> predicate,
            StateId<TState, THiddenState> state,
            string description = null)
        {
            return IfThenGoTo(d => Task.FromResult(predicate(d)), state, description);
        }

        public AsyncOperation<TState, THiddenState, TData> IfThenGoTo(
            Func<bool> predicate,
            StateId<TState, THiddenState> state,
            string description = null)
        {
            return IfThenGoTo(_ => Task.FromResult(predicate()), state, description);
        }

        public AsyncOperation<TState, THiddenState, TData> IfThenGoTo(
            Func<Task<bool>> taskFactory,
            StateId<TState, THiddenState> state,
            string description = null)
        {
            return IfThenGoTo(_ => taskFactory(), state, description);
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

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions) =>
                new List<TargetState<TState, THiddenState>>();
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

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions)
            {
                return Child.GetTargetStates(
                    Child.Description == null ? conditions : conditions.Concat(Enumerable.Repeat(Child.Description, 1)));
            }
        }

        private class InvokeHandlerWithData<TR> : AsyncOperationHandler
        {
            private readonly Func<TData, Task<TR>> _taskFactory;

            public InvokeHandlerWithData(State<TState, THiddenState> parent, Func<TData, Task<TR>> taskFactory)
            {
                _taskFactory = taskFactory;
                Child = new AsyncOperation<TState, THiddenState, TR>(parent);
            }

            public AsyncOperation<TState, THiddenState, TR> Child { get; }

            public override Task<State<TState, THiddenState>> ExecuteAsync()
            {
                throw new NotImplementedException();
            }

            public override async Task<State<TState, THiddenState>> ExecuteAsync(TData data)
            {
                var res = await _taskFactory(data);

                return await Child.ExecuteAsync(res);
            }

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions) => 
                Child.GetTargetStates(conditions);
        }

        private class InvokeHandler : AsyncOperationHandler
        {
            private readonly Func<TData, Task> _taskFactory;

            public InvokeHandler(State<TState, THiddenState> parent, Func<TData, Task> taskFactory)
            {
                _taskFactory = taskFactory;
                Child = new AsyncOperation<TState, THiddenState, TData>(parent);
            }

            public AsyncOperation<TState, THiddenState, TData> Child { get; }

            public override Task<State<TState, THiddenState>> ExecuteAsync()
            {
                throw new NotImplementedException();
            }

            public override async Task<State<TState, THiddenState>> ExecuteAsync(TData data)
            {
                await _taskFactory(data);

                return await Child.ExecuteAsync(data);
            }

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions) => 
                Child.GetTargetStates(conditions);
        }

        private class IfThenGoToHandlerWithData : AsyncOperationHandler
        {
            private readonly Func<TData, Task<bool>> _taskFactory;
            private readonly State<TState, THiddenState> _state;

            public IfThenGoToHandlerWithData(
                State<TState, THiddenState> parent,
                Func<TData, Task<bool>> taskFactory,
                State<TState, THiddenState> state,
                string description)
            {
                _taskFactory = taskFactory;
                _state = state;
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
                if (res)
                {
                    return _state;
                }

                return await Child.ExecuteAsync(data);
            }

            public override IList<TargetState<TState, THiddenState>> GetTargetStates(IEnumerable<string> conditions)
            {
                conditions = conditions.ToList();
                var targetState = new TargetState<TState, THiddenState>(
                    Child.Description == null ? conditions : conditions.Concat(Enumerable.Repeat(Child.Description, 1)),
                    _state);
                return Enumerable.Repeat(targetState, 1).Concat(Child.GetTargetStates(conditions)).ToList();
            }
        }
    }
}
