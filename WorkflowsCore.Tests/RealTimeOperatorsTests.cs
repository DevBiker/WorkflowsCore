﻿using System;
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
            Utilities.SystemClock = null;
        }

        [Fact]
        public async Task WaitForDateShouldWaitUntilSpecifiedDateInFuture()
        {
            var date = Utilities.SystemClock.Now.AddSeconds(1);
            await _workflow.WaitForDate(date);
            var now = Utilities.SystemClock.Now;
            Assert.True((now - date).TotalMilliseconds < 1000);
        }

        [Fact]
        public async Task WaitForDateShouldReturnImmediatelyForPastDates()
        {
            var before = Utilities.SystemClock.Now;
            var date = before.AddSeconds(-1);
            await _workflow.WaitForDate(date);
            var now = Utilities.SystemClock.Now;
            Assert.True((now - before).TotalMilliseconds < 100);
        }

        [Fact]
        public async Task WaitForDateShouldWaitInfinitelyForDateTimeMaxValue()
        {
            // ReSharper disable once PossibleNullReferenceException
            var ex = await Record.ExceptionAsync(
                () => Utilities.SetCurrentCancellationTokenTemporarily(
                    new CancellationTokenSource(10).Token,
                    () => _workflow.WaitForDate(DateTimeOffset.MaxValue)));

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
                        var t = _workflow.WaitForDate(Utilities.SystemClock.Now.AddSeconds(1));
                        Assert.NotEqual(TaskStatus.Canceled, t.Status);
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
                        await _workflow.WaitForDate(Utilities.SystemClock.Now.AddDays(-1));
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
                        var t = _workflow.WaitForDate(Utilities.SystemClock.Now.AddDays(60));
                        Assert.NotEqual(TaskStatus.Canceled, t.Status);
                        cts.Cancel();
                        await t;
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
