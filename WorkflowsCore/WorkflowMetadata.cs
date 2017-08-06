using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace WorkflowsCore
{
    public class WorkflowMetadata : IWorkflowMetadata
    {
        private readonly IDictionary<string, DataFieldMetadata> _dataFields;

        public WorkflowMetadata(Type workflowType)
        {
            WorkflowType = workflowType;
            _dataFields = GetDataFieldsProperties();
            var map = _dataFields.Where(k => !k.Value.IsTransient)
                .ToDictionary(k => k.Key, k => k.Value.PropertyInfo.PropertyType);

            DataFieldTypeMap = new ReadOnlyDictionary<string, Type>(map);
        }

        public Type WorkflowType { get; }

        public IReadOnlyDictionary<string, Type> DataFieldTypeMap { get; }

        public Dictionary<string, object> GetData(WorkflowBase workflow)
        {
            workflow.EnsureWorkflowTaskScheduler();
            return _dataFields.Where(k => !k.Value.IsTransient)
                .ToDictionary(k => k.Key, k => k.Value.PropertyInfo.GetValue(workflow));
        }

        public T TryGetDataField<T>(WorkflowBase workflow, string field)
        {
            workflow.EnsureWorkflowTaskScheduler();
            var metadata = GetDataFieldMetadata(field, nullIfNotFound: true);
            return metadata == null ? default(T) : (T)metadata.PropertyInfo.GetValue(workflow);
        }

        public T GetDataField<T>(WorkflowBase workflow, string field)
        {
            workflow.EnsureWorkflowTaskScheduler();
            return (T)GetDataFieldMetadata(field).PropertyInfo.GetValue(workflow);
        }

        public void SetDataField<T>(WorkflowBase workflow, string field, T value)
        {
            workflow.EnsureWorkflowTaskScheduler();
            GetDataFieldMetadata(field).PropertyInfo.SetValue(workflow, value);
        }

        public bool TrySetDataField<T>(WorkflowBase workflow, string field, T value) =>
            TrySetDataFieldCore(workflow, field, value, false);

        public void SetData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData)
        {
            workflow.EnsureWorkflowTaskScheduler();
            foreach (var pair in newData)
            {
                TrySetDataField(workflow, pair.Key, pair.Value);
            }
        }

        public Dictionary<string, object> GetTransientData(WorkflowBase workflow)
        {
            workflow.EnsureWorkflowTaskScheduler();
            var res = _dataFields.Where(k => k.Value.IsTransient)
                .ToDictionary(k => k.Key, k => k.Value.PropertyInfo.GetValue(workflow));
            foreach (var pair in workflow.TransientData.Data)
            {
                res[pair.Key] = pair.Value;
            }

            return res;
        }

        public T GetTransientDataField<T>(WorkflowBase workflow, string field)
        {
            workflow.EnsureWorkflowTaskScheduler();
            var metadata = GetTransientDataFieldMetadata(field);
            return metadata == null
                ? workflow.TransientData.GetDataField<T>(field)
                : (T)metadata.PropertyInfo.GetValue(workflow);
        }

        public void SetTransientDataField<T>(WorkflowBase workflow, string field, T value)
        {
            workflow.EnsureWorkflowTaskScheduler();
            var metadata = GetTransientDataFieldMetadata(field);
            if (metadata != null)
            {
                var propertyInfo = _dataFields[field].PropertyInfo;
                if (propertyInfo.CanWrite)
                {
                    propertyInfo.SetValue(workflow, value);
                }

                return;
            }

            workflow.TransientData.SetDataField(field, value);
        }

        public void SetTransientData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData)
        {
            workflow.EnsureWorkflowTaskScheduler();
            foreach (var pair in newData)
            {
                SetTransientDataField(workflow, pair.Key, pair.Value);
            }
        }

        public void SetDataOrTransientDataField<T>(WorkflowBase workflow, string field, T value)
        {
            workflow.EnsureWorkflowTaskScheduler();
            if (!TrySetDataFieldCore(workflow, field, value, true))
            {
                SetTransientDataField(workflow, field, value);
            }
        }

        public void SetDataOrTransientData(WorkflowBase workflow, IReadOnlyDictionary<string, object> newData)
        {
            foreach (var pair in newData)
            {
                SetDataOrTransientDataField(workflow, pair.Key, pair.Value);
            }
        }

        private IDictionary<string, DataFieldMetadata> GetDataFieldsProperties()
        {
            var properties = GetAllProperties()
                .Select(
                    p => new { PropertyInfo = p, DataFieldAttribute = p.GetCustomAttribute<DataFieldAttribute>(false) })
                .Where(i => i.DataFieldAttribute != null)
                .ToList();

            var duplicates =
                from t in properties
                join p in properties on t.PropertyInfo.Name equals p.PropertyInfo.Name into items
                where items.Count() > 1
                select new
                {
                    t.PropertyInfo.Name,
                    FirstType = t.PropertyInfo.DeclaringType?.FullName,
                    ConflictingType =
                        items.First(i => i.PropertyInfo.DeclaringType != t.PropertyInfo.DeclaringType)
                            .PropertyInfo.DeclaringType?.FullName
                };

            var d = duplicates.FirstOrDefault();
            if (d != null)
            {
                throw new ArgumentException(
                    $"Data field '{d.ConflictingType}.{d.Name}' conflicts with '{d.FirstType}.{d.Name}'");
            }

            return properties.ToDictionary(
                i => i.PropertyInfo.Name,
                i => new DataFieldMetadata
                {
                    PropertyInfo = i.PropertyInfo,
                    IsTransient = i.DataFieldAttribute.IsTransient
                });
        }

        private IList<PropertyInfo> GetAllProperties()
        {
            var properties = new List<PropertyInfo>();

            for (var type = WorkflowType; type != null; type = type.GetTypeInfo().BaseType)
            {
                var typeProperties = type
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(p => p.DeclaringType == type);
                properties.AddRange(typeProperties);
            }

            return properties;
        }

        private DataFieldMetadata GetDataFieldMetadata(
            string field,
            bool nullIfNotFound = false,
            bool ignoreTransientField = false)
        {
            DataFieldMetadata metadata;
            if (!_dataFields.TryGetValue(field, out metadata))
            {
                if (!nullIfNotFound)
                {
                    throw new ArgumentOutOfRangeException(nameof(field));
                }

                return null;
            }

            if (metadata.IsTransient)
            {
                if (ignoreTransientField)
                {
                    return null;
                }

                throw new ArgumentOutOfRangeException(nameof(field));
            }

            return _dataFields[field];
        }

        private DataFieldMetadata GetTransientDataFieldMetadata(string field)
        {
            DataFieldMetadata metadata;
            if (!_dataFields.TryGetValue(field, out metadata))
            {
                return null;
            }

            if (!metadata.IsTransient)
            {
                throw new ArgumentOutOfRangeException(nameof(field));
            }

            return _dataFields[field];
        }

        private bool TrySetDataFieldCore<T>(WorkflowBase workflow, string field, T value, bool ignoreTransientField)
        {
            workflow.EnsureWorkflowTaskScheduler();
            var dataFieldMetadata = GetDataFieldMetadata(
                field,
                nullIfNotFound: true,
                ignoreTransientField: ignoreTransientField);
            dataFieldMetadata?.PropertyInfo.SetValue(workflow, value);
            return dataFieldMetadata != null;
        }

        private class DataFieldMetadata
        {
            public PropertyInfo PropertyInfo { get; set; }

            public bool IsTransient { get; set; }
        }
    }
}
