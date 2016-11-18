using System;

namespace WorkflowsCore.Tests
{
    public class DummyWorkflowStateRepository : IWorkflowStateRepository
    {
        public virtual void SaveWorkflowData(WorkflowBase workflow)
        {
            throw new NotImplementedException();
        }

        public virtual void MarkWorkflowAsCompleted(WorkflowBase workflow)
        {
            throw new NotImplementedException();
        }

        public virtual void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception)
        {
            throw new NotImplementedException();
        }

        public virtual void MarkWorkflowAsCanceled(WorkflowBase workflow, Exception exception)
        {
            throw new NotImplementedException();
        }

        public virtual void MarkWorkflowAsSleeping(WorkflowBase workflow)
        {
            throw new NotImplementedException();
        }

        public virtual void MarkWorkflowAsInProgress(WorkflowBase workflow)
        {
            throw new NotImplementedException();
        }
    }
}
