using System;
using System.Threading;
using System.Threading.Tasks;
using WorkflowsCore.Time;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class TimeOperatorsTests
    {
        private readonly WorkflowBase _workflow = new TestWorkflow();

        public TimeOperatorsTests()
        {
            Utilities.TimeProvider = new TestingTimeProvider();
        }

        [Fact]
        public async Task WaitForDateShouldWaitUntilSpecifiedDateInFuture()
        {
            await Utilities.SetCurrentCancellationTokenTemporarily(
                new CancellationTokenSource(100).Token,
                async () =>
                {
                    var date = TestingTimeProvider.Current.Now.AddDays(1);
                    TestUtils.DoAsync(() => TestingTimeProvider.Current.SetCurrentTime(date));
                    await _workflow.WaitForDate(date);
                });
        }

        [Fact]
        public async Task WaitForDateShouldReturnImmediatelyForPastDates()
        {
            var before = TestingTimeProvider.Current.Now;
            var date = before.AddHours(-1);
            await _workflow.WaitForDate(date);
            var now = TestingTimeProvider.Current.Now;
            Assert.Equal(before, now);
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
                        await _workflow.WaitForDate(TestingTimeProvider.Current.Now.AddDays(1));
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
                        await _workflow.WaitForDate(TestingTimeProvider.Current.Now.AddDays(-1));
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
