using System;
using System.Collections.Concurrent;

namespace WorkflowsCore
{
    public class WorkflowMetadataCache : IWorkflowMetadataCache
    {
        private readonly ConcurrentDictionary<Type, WorkflowMetadata> _cache =
            new ConcurrentDictionary<Type, WorkflowMetadata>();

        public IWorkflowMetadata GetWorkflowMetadata(Type type) => _cache.GetOrAdd(type, t => new WorkflowMetadata(t));
    }
}
