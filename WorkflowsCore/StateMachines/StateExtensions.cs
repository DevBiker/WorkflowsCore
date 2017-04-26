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

        public static AsyncOperation<TState, THiddenState, DateTime> OnDate<TState, THiddenState>(
            this State<TState, THiddenState> state,
            Func<Task<DateTime>> dateTaskFactory,
            string description = null,
            Func<WorkflowBase, Task<bool>> bypassDatesFunc = null)
        {
            return state.OnAsync(
                async () =>
                {
                    var date = await dateTaskFactory();

                    // WaitForAny() is used to remove date from next activations dates after date was awaited
                    await Workflow.WaitForAny(() => Workflow.WaitForDate(date, bypassDatesFunc));
                    return date;
                },
                description);
        }

        public static AsyncOperation<TState, THiddenState, DateTime> OnDate<TState, THiddenState>(
            this State<TState, THiddenState> state,
            Func<DateTime> dateFactory,
            string description = null,
            Func<WorkflowBase, Task<bool>> bypassDatesFunc = null)
        {
            return state.OnDate(() => Task.FromResult(dateFactory()), description, bypassDatesFunc);
        }

        public static AsyncOperation<TState, THiddenState, NamedValues> OnAction<TState, THiddenState>(
            this State<TState, THiddenState> state,
            string action,
            string description = null)
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
                    () => operation);
        }

        public static AsyncOperation<TState, THiddenState, NamedValues> OnActions<TState, THiddenState>(
            this State<TState, THiddenState> state,
            string description,
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
                    () => operation);
        }

        public static AsyncOperation<TState, THiddenState> OnActionWithWasExecutedCheck<TState, THiddenState>(
            this State<TState, THiddenState> state,
            string action,
            string description = null)
        {
            return state
                .OnAsync(() => Workflow.WaitForActionWithWasExecutedCheck(action), description ?? $"On {action}")
                .If(() => Workflow.WasExecuted(action));
        }
    }
}
