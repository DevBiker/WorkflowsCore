using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowMetadataTests : BaseWorkflowTest<WorkflowMetadataTests.TestWorkflow>
    {
        private readonly WorkflowMetadata _workflowMetadata;

        public WorkflowMetadataTests()
        {
            Workflow = new TestWorkflow();
            _workflowMetadata = new WorkflowMetadata(Workflow.GetType());
            StartWorkflow();
        }

        [Fact]
        public async Task DataFieldTypeMapShouldContainAllNonTransientDataFieldsTypes()
        {
            Assert.True(_workflowMetadata.DataFieldTypeMap.ContainsKey("ActionStats"));
            Assert.True(_workflowMetadata.DataFieldTypeMap.ContainsKey("TestDataField"));
            Assert.True(_workflowMetadata.DataFieldTypeMap.ContainsKey("TestDataField2"));
            Assert.True(_workflowMetadata.DataFieldTypeMap.ContainsKey("VirtualDataField"));
            Assert.True(_workflowMetadata.DataFieldTypeMap.ContainsKey("VirtualDataField2"));
            Assert.False(_workflowMetadata.DataFieldTypeMap.ContainsKey("TestDataField3"));

            Assert.Equal(typeof(bool), _workflowMetadata.DataFieldTypeMap["TestDataField"]);
            Assert.Equal(typeof(object), _workflowMetadata.DataFieldTypeMap["TestDataField2"]);

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task SetDataFieldShouldUpdateThisField()
        {
            const string FieldName = "TestDataField2";
            await Workflow.DoWorkflowTaskAsync(
                w =>
                {
                    var curValue = _workflowMetadata.GetDataField<object>(w, FieldName);

                    Assert.Null(curValue);

                    var value = new object();
                    _workflowMetadata.SetDataField(w, FieldName, value);

                    curValue = _workflowMetadata.GetDataField<object>(w, FieldName);
                    Assert.Same(value, curValue);
                });
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task TryGetDataFieldForNonExistingFieldShouldReturnDefaultValue()
        {
            await Workflow.DoWorkflowTaskAsync(
                w => Assert.Equal(0, w.Metadata.TryGetDataField<int>(w, "Bad Field")));
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task SetGetDataFieldForUnkownFieldShouldThrowAoore()
        {
            await Workflow.DoWorkflowTaskAsync(
                w =>
                {
                    var ex = Record.Exception(() => _workflowMetadata.GetDataField<object>(w, "Bad Field"));
                    Assert.IsType<ArgumentOutOfRangeException>(ex);

                    ex = Record.Exception(() => _workflowMetadata.GetDataField<object>(w, "TestDataField3"));
                    Assert.IsType<ArgumentOutOfRangeException>(ex);

                    ex = Record.Exception(() => _workflowMetadata.SetDataField(w, "Bad Field", new object()));
                    Assert.IsType<ArgumentOutOfRangeException>(ex);

                    ex = Record.Exception(() => _workflowMetadata.SetDataField(w, "TestDataField3", new object()));
                    Assert.IsType<ArgumentOutOfRangeException>(ex);
                });
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task SetDataShouldOverrideValues()
        {
            await Workflow.DoWorkflowTaskAsync(
                w =>
                {
                    _workflowMetadata.SetData(
                        w,
                        new Dictionary<string, object> { ["TestDataField2"] = 1, ["TestDataField"] = true });
                    _workflowMetadata.SetData(w, new Dictionary<string, object> { ["TestDataField2"] = 2 });

                    var data = _workflowMetadata.GetData(w);

                    Assert.False(data.ContainsKey("TestDataField3"));
                    Assert.Equal(2, data["TestDataField2"]);
                    Assert.Equal(true, data["TestDataField"]);
                });
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task SetTransientDataFieldShouldUpdateThisField()
        {
            const string FieldName = "TestDataField3";
            await Workflow.DoWorkflowTaskAsync(
                w =>
                {
                    var curValue = _workflowMetadata.GetTransientDataField<int>(w, FieldName);
                    Assert.Equal(0, curValue);

                    _workflowMetadata.SetTransientDataField(w, FieldName, 3);

                    curValue = _workflowMetadata.GetTransientDataField<int>(w, FieldName);
                    Assert.Equal(3, curValue);
                });
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task GetTransientDataFieldForNonExistingFieldShouldReturnDefaultValue()
        {
            await Workflow.DoWorkflowTaskAsync(
                w => Assert.Equal(0, w.Metadata.GetTransientDataField<int>(w, "Bad Field")));
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task GetTransientDataFieldForExistingNonTransientFieldShouldThrowAoore()
        {
            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Workflow.DoWorkflowTaskAsync(w => w.Metadata.GetTransientDataField<object>(w, "TestDataField2")));

            Assert.IsType<ArgumentOutOfRangeException>(ex);
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task SetTransientDataFieldForFieldWithoutMetadataShouldStoreFieldInTheDictionary()
        {
            const string FieldName = "Id";
            await Workflow.DoWorkflowTaskAsync(
                w =>
                {
                    var curValue = _workflowMetadata.GetTransientDataField<int>(w, FieldName);
                    Assert.Equal(0, curValue);

                    _workflowMetadata.SetTransientDataField(w, FieldName, 3);

                    curValue = _workflowMetadata.GetTransientDataField<int>(w, FieldName);
                    Assert.Equal(3, curValue);
                });
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task SetTransientDataFieldForFieldWithoutMetadataShouldRemoveDictionaryItemIfValueIsDefault()
        {
            await Workflow.DoWorkflowTaskAsync(
                w =>
                {
                    _workflowMetadata.SetTransientDataField(w, "BypassDates", true);
                    _workflowMetadata.SetTransientDataField(w, "BypassDates", false);
                    Assert.False(_workflowMetadata.GetTransientData(w).ContainsKey("BypassDates"));
                });
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task SetTransientDataShouldOverrideValues()
        {
            await Workflow.DoWorkflowTaskAsync(
                w =>
                {
                    _workflowMetadata.SetTransientData(
                        w,
                        new Dictionary<string, object> { ["TestDataField3"] = 1, ["TestDataField4"] = true });
                    _workflowMetadata.SetTransientData(w, new Dictionary<string, object> { ["TestDataField3"] = 2 });

                    var data = _workflowMetadata.GetTransientData(w);

                    Assert.False(data.ContainsKey("TestDataField"));
                    Assert.Equal(2, data["TestDataField3"]);
                    Assert.Equal(true, data["TestDataField4"]);
                });
            await CancelWorkflowAsync();
        }

        [Fact]
        public async Task DuplicatingDataFieldsShouldBeReported()
        {
            var ex = Record.Exception(() => new WorkflowMetadata(typeof(BadWorkflow)));

            Assert.IsType<ArgumentException>(ex);

            await Workflow.StartedTask;
            await CancelWorkflowAsync();
        }

        public abstract class BaseWorkflow : WorkflowBase<int>
        {
            protected virtual int VirtualDataField { get; set; }

            [DataField]
            protected abstract int VirtualDataField2 { get; set; }
        }

        public class TestWorkflow : BaseWorkflow
        {
            [DataField]
            protected bool TestDataField { get; set; }

            [DataField]
            protected override int VirtualDataField { get; set; }

            protected override int VirtualDataField2 { get; set; }

            // ReSharper disable once UnusedMember.Local
            [DataField]
            private object TestDataField2 { get; set; }

            // ReSharper disable once UnusedMember.Local
            [DataField(IsTransient = true)]
            private int TestDataField3 { get; set; }

            protected override void OnStatesInit()
            {
            }

            protected override Task RunAsync() => Task.Delay(Timeout.Infinite, Utilities.CurrentCancellationToken);
        }

        private class BadWorkflow : TestWorkflow
        {
            // ReSharper disable once UnusedMember.Local
            [DataField(IsTransient = true)]
            private int ActionStats { get; set; }
        }
    }
}
