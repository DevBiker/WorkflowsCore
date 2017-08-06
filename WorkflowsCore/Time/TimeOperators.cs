using System;
using System.Threading;
using System.Threading.Tasks;

namespace WorkflowsCore.Time
{
    public static class TimeOperators
    {
        public static async Task WaitForDate(
            this WorkflowBase workflow,
            DateTimeOffset date,
            Func<WorkflowBase, Task<bool>> bypassDatesFunc = null)
        {
            var token = Utilities.CurrentCancellationToken;
            if (token.IsCancellationRequested)
            {
                var tcs = new TaskCompletionSource<bool>();
                tcs.SetCanceled();
                await tcs.Task;
            }

            if (date == DateTimeOffset.MaxValue)
            {
                await Task.Delay(Timeout.Infinite, token);
                return;
            }

            await workflow.RunViaWorkflowTaskScheduler(w => w.AddActivationDate(token, date), forceExecution: true);
            token.Register(
                () => workflow.RunViaWorkflowTaskScheduler(w => w.OnCancellationTokenCanceled(token), forceExecution: true),
                false);

            var now = Utilities.SystemClock.UtcNow;
            var diff = date - now;
            if (diff.TotalMilliseconds <= 0)
            {
                return;
            }

            var bypassDates = await (bypassDatesFunc?.Invoke(workflow) ?? Task.FromResult(false));
            if (bypassDates)
            {
                return;
            }

            var testingSystemClock = Utilities.SystemClock as ITestingSystemClock;
            if (testingSystemClock != null)
            {
                await WaitForDateWithTestingSystemClock(workflow, testingSystemClock, date);
                return;
            }

            var week = TimeSpan.FromDays(7);
            do
            {
                if (diff.TotalMilliseconds > week.TotalMilliseconds)
                {
                    diff = week;
                }

                await Task.Delay(diff, token);
                now = Utilities.SystemClock.UtcNow;
                diff = date - now;
            }
            while (diff.TotalMilliseconds > 0);

            if (token.IsCancellationRequested)
            {
                var tcs = new TaskCompletionSource<bool>();
                tcs.SetCanceled();
                await tcs.Task;
            }
        }

        private static Task WaitForDateWithTestingSystemClock(
            WorkflowBase workflow,
            ITestingSystemClock testingSystemClock,
            DateTimeOffset date)
        {
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<DateTimeOffset> handler = null;
            var cancellationToken = Utilities.CurrentCancellationToken;
            var registration = cancellationToken.Register(
                () =>
                {
                    // ReSharper disable once AccessToModifiedClosure
                    testingSystemClock.TimeAdjusted -= handler;
                    tcs.TrySetCanceled();
                },
                false);

            // ReSharper disable once MethodSupportsCancellation
            handler = (o, time) => workflow.RunViaWorkflowTaskScheduler(
                data =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (time >= date)
                    {
                        // ReSharper disable once AccessToModifiedClosure
                        testingSystemClock.TimeAdjusted -= handler;
                        if (tcs.TrySetResult(true))
                        {
                            registration.Dispose();
                        }
                    }
                },
                forceExecution: true).Wait();

            testingSystemClock.TimeAdjusted += handler;
            handler(null, testingSystemClock.UtcNow);
            return tcs.Task;
        }
    }
}
