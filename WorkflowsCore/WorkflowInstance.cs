using System.Collections.Generic;

namespace WorkflowsCore
{
    public class WorkflowInstance
    {
        public object Id { get; set; }

        public string WorkflowTypeName { get; set; }
        
        public IReadOnlyDictionary<string, object> Data { get; set; } 
    }
}
