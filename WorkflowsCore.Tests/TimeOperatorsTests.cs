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
            Utilities.SystemClock = new TestingSystemClock();
        }

        [Fact]
        public async Task WaitForDateShouldWaitUntilSpecifiedDateInFuture()
        {
            await Utilities.SetCurrentCancellationTokenTemporarily(
                new CancellationTokenSource(1000).Token,
                async () =>
                {
                    var date = TestingSystemClock.Current.Now.AddDays(1);
                    var t = _workflow.WaitForDate(date);
                    Assert.False(t.IsCompleted);
                    TestingSystemClock.Current.SetCurrentTime(date);
                    await t;
                });
        }

        [Fact]
        public async Task WaitForDateShouldReturnImmediatelyForPastDates()
        {
            var before = TestingSystemClock.Current.Now;
            var date = before.AddHours(-1);
            await _workflow.WaitForDate(date);
            var now = TestingSystemClock.Current.Now;
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
                        var t = _workflow.WaitForDate(TestingSystemClock.Current.Now.AddDays(1));
                        Assert.False(t.IsCompleted);
                        cts.Cancel();
                        await t;
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
                        await _workflow.WaitForDate(TestingSystemClock.Current.Now.AddDays(-1));
                    }));

            Assert.IsType<TaskCanceledException>(ex);
        }

        [Fact]
        public async Task WaitForDateShouldAffectNextActivationDateUntilParentTokenIsCancelled()
        {
            var cts = new CancellationTokenSource();

            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Utilities.SetCurrentCancellationTokenTemporarily(
                    cts.Token,
                    async () =>
                    {
                        var expected = TestingSystemClock.Current.Now.AddHours(1);
                        var t = _workflow.WaitForDate(expected);
                        var date = await _workflow.GetTransientDataFieldAsync<DateTimeOffset?>(
                            "NextActivationDate",
                            forceExecution: true);
                        Assert.Equal(expected, date);
                        cts.Cancel();
                        await t;
                    }));

            var newDate = await _workflow.GetTransientDataFieldAsync<DateTimeOffset?>(
                "NextActivationDate",
                forceExecution: true);
            Assert.Null(newDate);
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
