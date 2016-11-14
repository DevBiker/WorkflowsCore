using System;

namespace WorkflowsCore
{
    public class WorkflowMetadataCache : IWorkflowMetadataCache
    {
        public WorkflowMetadata GetWorkflowMetadata(WorkflowBase workflow)
        {
            throw new NotImplementedException();
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
