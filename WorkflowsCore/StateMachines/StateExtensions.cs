using System;
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
                    await Workflow.WaitForDate(date, bypassDatesFunc);
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
            return state.AllowActions(action)
                .OnAsync(() => Workflow.WaitForAction(action), description)
                .If(() => Workflow.IsActionAllowed(action)); // TODO:
        }

        public static AsyncOperation<TState, THiddenState> OnActionWithWasExecutedCheck<TState, THiddenState>(
            this State<TState, THiddenState> state,
            string action,
            string description = null)
        {
            return state.AllowActions(action)
                .OnAsync(() => Workflow.WaitForActionWithWasExecutedCheck(action), description)
                .If(() => Workflow.IsActionAllowed(action)); // TODO:
        }
    }
}
