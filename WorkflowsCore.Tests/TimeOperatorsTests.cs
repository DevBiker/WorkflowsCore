using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowsCore.Time;

namespace WorkflowsCore.Tests
{
    [TestClass]
    public class TimeOperatorsTests
    {
        [TestInitialize]
        public void TestInitialize()
        {
            Utilities.TimeProvider = new TestingTimeProvider();
        }

        [TestMethod]
        public async Task WaitForDateShouldWaitUntilSpecifiedDateInFuture()
        {
            await Utilities.SetCurrentCancellationTokenTemporarily(
                new CancellationTokenSource(100).Token,
                async () =>
                {
                    var date = TestingTimeProvider.Current.Now.AddDays(1);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Delay(10).ContinueWith(t => TestingTimeProvider.Current.SetCurrentTime(date));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    await new TestWorkflow(() => null).WaitForDate(date);
                });
        }

        [TestMethod]
        public async Task WaitForDateShouldReturnImmediatelyForPastDates()
        {
            var before = TestingTimeProvider.Current.Now;
            var date = before.AddHours(-1);
            await new TestWorkflow(() => null).WaitForDate(date);
            var now = TestingTimeProvider.Current.Now;
            Assert.AreEqual(before, now);
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
                    await new TestWorkflow(() => null).WaitForDate(TestingTimeProvider.Current.Now.AddDays(1));
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
                    await new TestWorkflow(() => null).WaitForDate(TestingTimeProvider.Current.Now.AddDays(-1));
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
