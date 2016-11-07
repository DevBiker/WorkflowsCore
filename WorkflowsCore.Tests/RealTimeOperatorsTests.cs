using System;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class RealTimeOperatorsTests
    {
        private readonly WorkflowBase _workflow = new TestWorkflow();

        public RealTimeOperatorsTests()
        {
            Utilities.TimeProvider = null;
        }

        [Fact]
        public async Task WaitForDateShouldWaitUntilSpecifiedDateInFuture()
        {
            var date = Utilities.TimeProvider.Now.AddSeconds(1);
            await _workflow.WaitForDate(date);
            var now = Utilities.TimeProvider.Now;
            Assert.True((now - date).TotalMilliseconds < 50);
        }

        [Fact]
        public async Task WaitForDateShouldReturnImmediatelyForPastDates()
        {
            var before = Utilities.TimeProvider.Now;
            var date = before.AddSeconds(-1);
            await _workflow.WaitForDate(date);
            var now = Utilities.TimeProvider.Now;
            Assert.True((now - before).TotalMilliseconds < 10);
        }

        [Fact]
        public async Task WaitForDateShouldWaitInfinitelyForDateTimeMaxValue()
        {
            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Utilities.SetCurrentCancellationTokenTemporarily(
                    new CancellationTokenSource(10).Token,
                    () => _workflow.WaitForDate(DateTime.MaxValue)));

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task WaitForDateShouldBeCanceledIfWorkflowCanceled()
        {
            var cts = new CancellationTokenSource();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Utilities.SetCurrentCancellationTokenTemporarily(
                    cts.Token,
                    async () =>
                    {
                        TestUtils.DoAsync(() => cts.Cancel());
                        await _workflow.WaitForDate(Utilities.TimeProvider.Now.AddSeconds(1));
                    }));

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task WaitForDateShouldBeCanceledImmediatelyIfWorkflowIsAlreadyCanceled()
        {
            var cts = new CancellationTokenSource();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Utilities.SetCurrentCancellationTokenTemporarily(
                    cts.Token,
                    async () =>
                    {
                        cts.Cancel();
                        await _workflow.WaitForDate(Utilities.TimeProvider.Now.AddDays(-1));
                    }));

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task WaitForDateShouldWorkForFarFutureDates()
        {
            var cts = new CancellationTokenSource();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Utilities.SetCurrentCancellationTokenTemporarily(
                    cts.Token,
                    async () =>
                    {
                        TestUtils.DoAsync(() => cts.Cancel());
                        await _workflow.WaitForDate(Utilities.TimeProvider.Now.AddDays(60));
                    }));

            Assert.IsType<TaskCanceledException>(ex);
        }

        private class TestWorkflow : WorkflowBase<int>
        {
            protected override void OnStatesInit()
            {
                throw new NotImplementedException();
            }

            protected override Task RunAsync()
            {
                throw new NotImplementedException();
            }
        }
    }
}
