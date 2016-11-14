﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkflowsCore
{
    public enum WorkflowStatus
    {
        InProgress = 0,
        Completed = 1,
        Canceled = 2,
        Failed = 3,
        Sleeping = 4
    }

    /// <summary>Repository for managing workflow state and data</summary>
    /// <remarks>Methods of the interface are always called within workflow <c>TaskScheduler</c></remarks>>
    public interface IWorkflowStateRepository
    {
        void SaveWorkflowData(WorkflowBase workflow);

        void MarkWorkflowAsCompleted(WorkflowBase workflow);

        void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception);

        void MarkWorkflowAsCanceled(WorkflowBase workflow);

        void MarkWorkflowAsSleeping(WorkflowBase workflow);

        void MarkWorkflowAsInProgress(WorkflowBase workflow);
    }

    public interface IWorkflowMetadata
    {
        Type WorkflowType { get; }

        IReadOnlyDictionary<string, object> GetData(WorkflowBase workflow);

        T GetDataField<T>(WorkflowBase workflow, string field);

        void SetDataField<T>(WorkflowBase workflow, string field, T value);

        void SetData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData);

        IReadOnlyDictionary<string, object> GetTransientData(WorkflowBase workflow);

        T GetTransientDataField<T>(WorkflowBase workflow, string field);

        void SetTransientDataField<T>(WorkflowBase workflow, string field, T value);

        void SetTransientData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData);
    }

    public interface IWorkflowMetadataCache
    {
        IWorkflowMetadata GetWorkflowMetadata(WorkflowBase workflow);

        IWorkflowMetadata GetWorkflowMetadata(string fullTypeName);

        IWorkflowMetadata GetWorkflowMetadata(Type type);
    }

    public interface IDependencyInjectionContainer
    {
        object Resolve(Type type);
    }

    public interface IWorkflowEngine
    {
        Task LoadingTask { get; }

        IList<WorkflowBase> RunningWorkflows { get; }

        WorkflowBase CreateWorkflow(string fullTypeName);

        WorkflowBase CreateWorkflow(string fullTypeName, IReadOnlyDictionary<string, object> initialWorkflowData);

        Task LoadAndExecuteActiveWorkflowsAsync();

        WorkflowBase GetActiveWorkflowById(object id);
    }

    public interface IWorkflowRepository
    {
        /// <summary>
        /// It should return workflows in progress and faulted. It should not return sleeping workflows.
        /// </summary>
        IList<WorkflowInstance> GetActiveWorkflows();

        WorkflowInstance GetSleepingOrFaultedWorkflowById(object workflowId);

        WorkflowStatus GetWorkflowStatusById(object workflowId);
    }

    public class WorkflowInstance
    {
        public object Id { get; set; }

        public string WorkflowTypeName { get; set; }

        public IReadOnlyDictionary<string, object> Data { get; set; }
    }

    public class StateStats
    {
        public StateStats(int counter = 0)
        {
            EnteredCounter = counter;
            IgnoreSuppressionEnteredCounter = counter;
        }

        public int EnteredCounter { get; set; }

        public int IgnoreSuppressionEnteredCounter { get; set; }
    }
}
