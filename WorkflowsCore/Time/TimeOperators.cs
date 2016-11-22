using System;
using System.Threading;
using System.Threading.Tasks;

namespace WorkflowsCore.Time
{
    public static class TimeOperators
    {
        public static async Task WaitForDate(
            this WorkflowBase workflow,
            DateTime date,
            Func<WorkflowBase, Task<bool>> bypassDatesFunc = null)
        {
            var token = Utilities.CurrentCancellationToken;
            if (token.IsCancellationRequested)
            {
                var tcs = new TaskCompletionSource<bool>();
                tcs.SetCanceled();
                await tcs.Task;
            }

            if (date == DateTime.MaxValue)
            {
                await Task.Delay(Timeout.Infinite, token);
                return;
            }

            await workflow.RunViaWorkflowTaskScheduler(w => w.AddActivationDate(token, date), forceExecution: true);
            token.Register(
                () => workflow.RunViaWorkflowTaskScheduler(w => w.OnCancellationTokenCancelled(token), forceExecution: true),
                false);

            var now = Utilities.TimeProvider.Now;
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

            var testingTimeProvider = Utilities.TimeProvider as ITestingTimeProvider;
            if (testingTimeProvider != null)
            {
                await WaitForDateWithTestingTimeProvider(workflow, testingTimeProvider, date);
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
                now = Utilities.TimeProvider.Now;
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

        private static Task WaitForDateWithTestingTimeProvider(
            WorkflowBase workflow,
            ITestingTimeProvider testingTimeProvider,
            DateTime date)
        {
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<DateTime> handler = null;
            var cancellationToken = Utilities.CurrentCancellationToken;
            var registration = cancellationToken.Register(
                () =>
                {
                    // ReSharper disable once AccessToModifiedClosure
                    testingTimeProvider.TimeAdjusted -= handler;
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
                        testingTimeProvider.TimeAdjusted -= handler;
                        if (tcs.TrySetResult(true))
                        {
                            registration.Dispose();
                        }
                    }
                },
                forceExecution: true).Wait();

            testingTimeProvider.TimeAdjusted += handler;
            handler(null, testingTimeProvider.Now);
            return tcs.Task;
        }
    }
}
