using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;

namespace WorkflowsCore.StateMachines
{
    public static class StateExtensions
    {
        private static readonly AsyncLocal<WorkflowBase> AsyncWorkflow = new AsyncLocal<WorkflowBase>();

        public static WorkflowBase Workflow
        {
            get { return AsyncWorkflow.Value; }

            private set { AsyncWorkflow.Value = value; }
        }

        public static void SetWorkflowTemporarily(WorkflowBase workflow, Action action)
        {
            var old = Workflow;
            Workflow = workflow;
            try
            {
                action();
            }
            finally
            {
                Workflow = old;
            }
        }

        public static T SetWorkflowTemporarily<T>(WorkflowBase workflow, Func<T> func)
        {
            var old = Workflow;
            Workflow = workflow;
            try
            {
                return func();
            }
            finally
            {
                Workflow = old;
            }
        }

        public static AsyncOperation<TState, TInternalState, DateTime> OnDate<TState, TInternalState>(
            this State<TState, TInternalState> state,
            Func<Task<DateTime>> dateTaskFactory,
            string description = null,
            Func<WorkflowBase, Task<bool>> bypassDatesFunc = null,
            bool isHidden = false)
        {
            return state.OnAsync(
                async () =>
                {
                    var date = await dateTaskFactory();

                    // WaitForAny() is used to remove date from next activations dates after date was awaited
                    await Workflow.WaitForAny(() => Workflow.WaitForDate(date, bypassDatesFunc));
                    return date;
                },
                description,
                isHidden: isHidden);
        }

        public static AsyncOperation<TState, TInternalState, DateTime> OnDate<TState, TInternalState>(
            this State<TState, TInternalState> state,
            Func<DateTime> dateFactory,
            string description = null,
            Func<WorkflowBase, Task<bool>> bypassDatesFunc = null,
            bool isHidden = false)
        {
            return state.OnDate(() => Task.FromResult(dateFactory()), description, bypassDatesFunc, isHidden);
        }

        public static AsyncOperation<TState, TInternalState, NamedValues> OnAction<TState, TInternalState>(
            this State<TState, TInternalState> state,
            string action,
            string description = null,
            bool isHidden = false)
        {
            IDisposable operation = null;
            return state.AllowActions(action)
                .OnAsync(
                    async () =>
                    {
                        var parameters = await Workflow.WaitForAction(action, exportOperation: true);
                        operation = parameters.GetDataField<IDisposable>("ActionOperation");
                        if (operation == null)
                        {
                            throw new InvalidOperationException();
                        }

                        return parameters;
                    },
                    description ?? $"On {action}",
                    () => operation,
                    isHidden);
        }

        public static AsyncOperation<TState, TInternalState, NamedValues> OnActions<TState, TInternalState>(
            this State<TState, TInternalState> state,
            string description,
            bool isHidden,
            params string[] actions)
        {
            IDisposable operation = null;
            return state.AllowActions(actions)
                .OnAsync(
                    async () =>
                    {
                        var actionsParameters = new NamedValues[actions.Length];
                        var actionsTaskFactories =
                            actions.Select(
                                (a, i) => (Func<Task>)(async () =>
                                    actionsParameters[i] = await Workflow.WaitForAction(a, exportOperation: true)))
                                .ToArray();

                        var index = await Workflow.WaitForAny(actionsTaskFactories);
                        var parameters = actionsParameters[index];
                        operation = parameters.GetDataField<IDisposable>("ActionOperation");
                        if (operation == null)
                        {
                            throw new InvalidOperationException();
                        }

                        return parameters;
                    },
                    description,
                    () => operation,
                    isHidden);
        }

        public static AsyncOperation<TState, TInternalState> OnActionWithWasExecutedCheck<TState, TInternalState>(
            this State<TState, TInternalState> state,
            string action,
            string description = null,
            bool isHidden = false)
        {
            return state
                .OnAsync(
                    () => Workflow.WaitForActionWithWasExecutedCheck(action),
                    description ?? $"On {action}",
                    isHidden: isHidden)
                .If(() => Workflow.WasExecuted(action));
        }
    }
}
