using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkflowsCore
{
    public enum WorkflowStatus
    {
        InProgress = 0,
        Completed = 1,
        Canceled = 2,
        Failed = 3
    }

    /// <summary>Repository for managing workflow state and data</summary>
    /// <remarks>Methods of the interface are always called within workflow <c>TaskScheduler</c></remarks>
    public interface IWorkflowStateRepository
    {
        void SaveWorkflowData(WorkflowBase workflow, DateTimeOffset? nextActivationDate);

        void MarkWorkflowAsCompleted(WorkflowBase workflow);

        void MarkWorkflowAsFailed(WorkflowBase workflow, Exception exception);

        void MarkWorkflowAsCanceled(WorkflowBase workflow, Exception exception);
    }

    public interface IWorkflowMetadata
    {
        Type WorkflowType { get; }

        IReadOnlyDictionary<string, Type> DataFieldTypeMap { get; }

        Dictionary<string, object> GetData(WorkflowBase workflow);

        T TryGetDataField<T>(WorkflowBase workflow, string field);

        T GetDataField<T>(WorkflowBase workflow, string field);

        void SetDataField<T>(WorkflowBase workflow, string field, T value);

        bool TrySetDataField<T>(WorkflowBase workflow, string field, T value);

        void SetData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData);

        Dictionary<string, object> GetTransientData(WorkflowBase workflow);

        T GetTransientDataField<T>(WorkflowBase workflow, string field);

        void SetTransientDataField<T>(WorkflowBase workflow, string field, T value);

        void SetTransientData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData);

        void SetDataOrTransientDataField<T>(WorkflowBase workflow, string field, T value);

        void SetDataOrTransientData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData);
    }

    public interface IWorkflowMetadataCache
    {
        IWorkflowMetadata GetWorkflowMetadata(Type type);
    }

    public interface IDependencyInjectionContainer
    {
        object Resolve(Type type);
    }

    public interface IWorkflowEngine
    {
        IList<WorkflowBase> RunningWorkflows { get; }

        Task PreloadWorkflowsTask { get; }

        WorkflowBase CreateWorkflow(string fullTypeName);

        WorkflowBase CreateWorkflow(string fullTypeName, IReadOnlyDictionary<string, object> initialWorkflowData);

        WorkflowBase CreateWorkflow(
            string fullTypeName,
            IReadOnlyDictionary<string, object> initialWorkflowData,
            IReadOnlyDictionary<string, object> initialWorkflowTransientData);

        Task LoadAndExecuteActiveWorkflowsAsync();

        Task LoadAndExecuteActiveWorkflowsAsync(int preloadHours);

        Task<WorkflowBase> GetActiveWorkflowByIdAsync(object id);
    }

    public interface IWorkflowRepository
    {
        /// <summary>
        /// It should return workflows in progress and faulted.
        /// </summary>
        /// <param name="maxActivationDate">Workflows with next activation date greater than <c>maxActivationDate</c> should be ignored.</param>
        /// <param name="ignoreWorkflowsIds">IDs of workflows to ignore.</param>
        Task<IList<WorkflowInstance>> GetActiveWorkflowsAsync(DateTimeOffset maxActivationDate, IEnumerable<object> ignoreWorkflowsIds);

        /// <summary>
        /// It should return workflows in progress and faulted.
        /// </summary>
        Task<WorkflowInstance> GetActiveWorkflowByIdAsync(object workflowId);
    }

    public class WorkflowInstance
    {
        public WorkflowInstance(
            object id, WorkflowStatus status, string workflowTypeName, IReadOnlyDictionary<string, object> data)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Status = status;
            WorkflowTypeName = workflowTypeName ?? throw new ArgumentNullException(nameof(workflowTypeName));
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public object Id { get; private set; }

        public WorkflowStatus Status { get; private set; }

        public string WorkflowTypeName { get; private set; }

        public IReadOnlyDictionary<string, object> Data { get; private set; }
    }

    public class Event
    {
        public Event(string eventName, IReadOnlyDictionary<string, string> parameters)
        {
            EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
            CreatedOn = Utilities.SystemClock.UtcNow;
            Parameters = parameters;
        }

        public string EventName { get; private set; }

        public DateTimeOffset CreatedOn { get; private set; }

        public IReadOnlyDictionary<string, string> Parameters { get; private set; }
    }
}
