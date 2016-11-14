using System;
using System.Collections.Generic;

namespace WorkflowsCore
{
    public class WorkflowMetadata
    {
        public Type WorkflowType { get; }

        public NamedValues GetData(WorkflowBase workflow)
        {
            throw new NotImplementedException();
        }

        public T GetDataField<T>(WorkflowBase workflow, string field)
        {
            throw new NotImplementedException();
        }

        public void SetDataField<T>(WorkflowBase workflow, string field, T value)
        {
            throw new NotImplementedException();
        }

        public void SetData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData)
        {
            throw new NotImplementedException();
        }

        public NamedValues GetTransientData(WorkflowBase workflow)
        {
            throw new NotImplementedException();
        }

        public T GetTransientDataField<T>(WorkflowBase workflow, string field)
        {
            throw new NotImplementedException();
        }

        public void SetTransientDataField<T>(WorkflowBase workflow, string field, T value)
        {
            throw new NotImplementedException();
        }

        public void SetTransientData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData)
        {
            throw new NotImplementedException();
        }
    }
}
