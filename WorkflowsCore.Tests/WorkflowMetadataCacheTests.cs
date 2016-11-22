using Xunit;

namespace WorkflowsCore.Tests
{
    public class WorkflowMetadataCacheTests
    {
        private readonly WorkflowMetadataCache _metadadaCache;

        public WorkflowMetadataCacheTests()
        {
            _metadadaCache = new WorkflowMetadataCache();
        }

        [Fact]
        public void MetadataShouldBeCached()
        {
            var metadata1 = _metadadaCache.GetWorkflowMetadata(typeof(WorkflowBase));
            var metadata2 = _metadadaCache.GetWorkflowMetadata(typeof(WorkflowBase));
            Assert.Same(metadata1, metadata2);
        } 
    }
}
