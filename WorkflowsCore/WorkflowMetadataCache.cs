using System;

namespace WorkflowsCore
{
    public class WorkflowMetadataCache : IWorkflowMetadataCache
    {
        public IWorkflowMetadata GetWorkflowMetadata(string fullTypeName)
        {
            throw new NotImplementedException();
        }

        public IWorkflowMetadata GetWorkflowMetadata(Type type)
        {
            return new WorkflowMetadata(type);
        }
    }
}
