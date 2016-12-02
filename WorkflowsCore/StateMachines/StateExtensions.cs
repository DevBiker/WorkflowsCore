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

        public static bool HasChild<T>(this State<T> state, State<T> child) => false;

        public static AsyncOperation<T, DateTime> OnDate<T>(
            this State<T> state,
            Func<Task<DateTime>> dateTaskFactory,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public static AsyncOperation<T, DateTime> OnDate<T>(
            this State<T> state,
            Func<DateTime> dateFactory,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public static AsyncOperation<T, NamedValues> OnAction<T>(
            this State<T> state,
            string action,
            string description = null)
        {
            throw new NotImplementedException();
        }

        public static AsyncOperation<T> OnActionWithWasExecutedCheck<T>(
            this State<T> state,
            string action,
            string description = null)
        {
            throw new NotImplementedException();
        }
    }
}
