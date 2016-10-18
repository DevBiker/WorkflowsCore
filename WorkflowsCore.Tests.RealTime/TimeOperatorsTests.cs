using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowsCore.Time;

namespace WorkflowsCore.Tests.RealTime
{
    [TestClass]
    public class TimeOperatorsTests
    {
        [TestMethod]
        public async Task WaitForDateShouldWaitUntilSpecifiedDateInFuture()
        {
            await Utilities.SetCurrentCancellationTokenTemporarily(
                new CancellationTokenSource().Token,
                async () =>
                {
                    var date = Utilities.TimeProvider.Now.AddSeconds(1);
                    await new TestWorkflow(() => null).WaitForDate(date);
                    var now = Utilities.TimeProvider.Now;
                    Assert.IsTrue((now - date).TotalMilliseconds < 50);
                });
        }

        [TestMethod]
        public async Task WaitForDateShouldReturnImmediatelyForPastDates()
        {
            var before = Utilities.TimeProvider.Now;
            var date = before.AddSeconds(-1);
            await new TestWorkflow(() => null).WaitForDate(date);
            var now = Utilities.TimeProvider.Now;
            Assert.IsTrue((now - before).TotalMilliseconds < 10);
        }

        [TestMethod]
        [ExpectedException(typeof(TaskCanceledException))]
        public async Task WaitForDateShouldWaitInfinitelyForDateTimeMaxValue()
        {
            await Utilities.SetCurrentCancellationTokenTemporarily(
                new CancellationTokenSource(10).Token,
                async () =>
                {
                    await new TestWorkflow(() => null).WaitForDate(DateTime.MaxValue);
                    Assert.Fail();
                });
        }

        [TestMethod]
        [ExpectedException(typeof(TaskCanceledException))]
        public async Task WaitForDateShouldBeCanceledIfWorkflowCanceled()
        {
            var cts = new CancellationTokenSource();
            await Utilities.SetCurrentCancellationTokenTemporarily(
                cts.Token,
                async () =>
                {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Delay(1).ContinueWith(t => cts.Cancel());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    await new TestWorkflow(() => null).WaitForDate(Utilities.TimeProvider.Now.AddSeconds(1));
                    Assert.Fail();
                });
        }

        [TestMethod]
        [ExpectedException(typeof(TaskCanceledException))]
        public async Task WaitForDateShouldBeCanceledImmediatelyIfWorkflowIsAlreadyCanceled()
        {
            var cts = new CancellationTokenSource();
            await Utilities.SetCurrentCancellationTokenTemporarily(
                cts.Token,
                async () =>
                {
                    cts.Cancel();
                    await new TestWorkflow(() => null).WaitForDate(Utilities.TimeProvider.Now.AddDays(-1));
                    Assert.Fail();
                });
        }

        [TestMethod]
        [ExpectedException(typeof(TaskCanceledException))]
        public async Task WaitForDateShouldWorkForFarFutureDates()
        {
            var cts = new CancellationTokenSource();
            await Utilities.SetCurrentCancellationTokenTemporarily(
                cts.Token,
                async () =>
                {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Delay(10).ContinueWith(t => cts.Cancel());
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    await new TestWorkflow(() => null).WaitForDate(Utilities.TimeProvider.Now.AddDays(60));
                    Assert.Fail();
                });
        }

        private class TestWorkflow : WorkflowBase<int>
        {
            public TestWorkflow(
                Func<IWorkflowStateRepository> workflowRepoFactory,
                CancellationToken parentCancellationToken = default(CancellationToken))
                : base(workflowRepoFactory, parentCancellationToken)
            {
            }

            protected override void OnStatesInit()
            {
            }

            protected override Task RunAsync()
            {
                throw new NotImplementedException();
            }
        }
    }
}
