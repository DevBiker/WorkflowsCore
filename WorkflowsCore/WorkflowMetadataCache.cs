using System;

namespace WorkflowsCore
{
    public class WorkflowMetadataCache : IWorkflowMetadataCache
    {
        public WorkflowMetadata GetWorkflowMetadata(WorkflowBase workflow)
        {
            return new WorkflowMetadata();
        }

        public WorkflowMetadata GetWorkflowMetadata(string fullTypeName)
        {
            throw new NotImplementedException();
        }

        public WorkflowMetadata GetWorkflowMetadata(Type type)
        {
            throw new NotImplementedException();
        }
    }
}
