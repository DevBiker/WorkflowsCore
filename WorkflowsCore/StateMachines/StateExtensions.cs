using System;
using System.Threading;
using System.Threading.Tasks;

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
            string description = null)
        {
            throw new NotImplementedException();
        }

        public static AsyncOperation<TState, DateTime> OnDate<TState, THiddenState>(
            this State<TState, THiddenState> state,
            Func<DateTime> dateFactory,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public static AsyncOperation<TState, NamedValues> OnAction<TState, THiddenState>(
            this State<TState, THiddenState> state,
            string action,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public static AsyncOperation<TState, THiddenState> OnActionWithWasExecutedCheck<TState, THiddenState>(
            this State<TState, THiddenState> state,
            string action,
            string description = null)
        {
            throw new NotImplementedException();
        }
    }
}
