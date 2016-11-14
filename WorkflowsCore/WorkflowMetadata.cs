using System;
using System.Collections.Generic;

namespace WorkflowsCore
{
    public class WorkflowMetadata : IWorkflowMetadata
    {
        public Type WorkflowType { get; }

        public IReadOnlyDictionary<string, object> GetData(WorkflowBase workflow)
        {
            workflow.EnsureWorkflowTaskScheduler();
            return workflow.Data;
        }

        public T GetDataField<T>(WorkflowBase workflow, string field)
        {
            workflow.EnsureWorkflowTaskScheduler();
            return workflow.GetDataField<T>(field);
        }

        public void SetDataField<T>(WorkflowBase workflow, string field, T value)
        {
            workflow.EnsureWorkflowTaskScheduler();
            workflow.SetDataField(field, value);
        }

        public void SetData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData)
        {
            workflow.EnsureWorkflowTaskScheduler();
            workflow.SetData(newData);
        }

        public IReadOnlyDictionary<string, object> GetTransientData(WorkflowBase workflow)
        {
            workflow.EnsureWorkflowTaskScheduler();
            return workflow.TransientData;
        }

        public T GetTransientDataField<T>(WorkflowBase workflow, string field)
        {
            workflow.EnsureWorkflowTaskScheduler();
            return workflow.GetTransientDataField<T>(field);
        }

        public void SetTransientDataField<T>(WorkflowBase workflow, string field, T value)
        {
            workflow.EnsureWorkflowTaskScheduler();
            workflow.SetTransientDataField(field, value);
        }

        public void SetTransientData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData)
        {
            workflow.EnsureWorkflowTaskScheduler();
            workflow.SetTransientData(newData);
        }
    }
}
