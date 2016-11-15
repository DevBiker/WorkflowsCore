using System;

namespace WorkflowsCore
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class DataFieldAttribute : Attribute
    {
        public bool IsTransient { get; set; }
    }
}
