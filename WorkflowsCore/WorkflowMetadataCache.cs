using System;

namespace WorkflowsCore
{
    public class WorkflowMetadataCache : IWorkflowMetadataCache
    {
        public IWorkflowMetadata GetWorkflowMetadata(WorkflowBase workflow)
        {
            return new WorkflowMetadata();
        }

        public IWorkflowMetadata GetWorkflowMetadata(string fullTypeName)
        {
            throw new NotImplementedException();
        }

        public IWorkflowMetadata GetWorkflowMetadata(Type type)
        {
            throw new NotImplementedException();
        }
    }
}
