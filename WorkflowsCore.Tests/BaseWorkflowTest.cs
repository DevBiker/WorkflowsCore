using System;
using System.Threading.Tasks;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class BaseWorkflowTest<T> : IDisposable 
        where T : WorkflowBase
    {
        private bool _wasStarted;
        private T _workflow;
        private bool _wasCanceled;
        private bool _wasCompleted;

        public T Workflow
        {
            get
            {
                return _workflow;
            }

            set
            {
                Assert.False(_wasStarted);
                _workflow = value;
            }
        }

        public void StartWorkflow(Action beforeWorkflowStarted = null)
        {
            Assert.NotNull(Workflow);
            _wasStarted = true;
            Workflow.StartWorkflow(beforeWorkflowStarted: beforeWorkflowStarted);
        }

        public async Task CancelWorkflowAsync()
        {
            Assert.True(_wasStarted);
            Assert.Equal(TaskStatus.RanToCompletion, Workflow.StartedTask.Status);

            Workflow.CancelWorkflow();
            _wasCanceled = true;

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => Workflow.CompletedTask.WaitWithTimeout(1000));

            Assert.IsType<TaskCanceledException>(ex);
        }

        public async Task WaitUntilWorkflowCompleted()
        {
            Assert.True(_wasStarted);
            _wasCompleted = true;
            await Workflow.CompletedTask;
        }

        public async Task WaitUntilWorkflowFailed<TEx>()
        {
            Assert.True(_wasStarted);

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(() => Workflow.CompletedTask);

            Assert.IsType(typeof(TEx), ex);
        }

        public virtual void Dispose()
        {
            if (!_wasStarted)
            {
                return;
            }

            var taskStatus = _wasCompleted
                ? TaskStatus.RanToCompletion
                : (_wasCanceled ? TaskStatus.Canceled : TaskStatus.Faulted);

            Assert.Equal(taskStatus, Workflow.CompletedTask.Status);
        }
    }
}
