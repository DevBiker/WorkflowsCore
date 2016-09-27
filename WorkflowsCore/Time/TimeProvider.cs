using System;

namespace WorkflowsCore.Time
{
    public interface ITimeProvider
    {
        DateTime Now { get; }
    }

    public interface ITestingTimeProvider : ITimeProvider
    {
        event EventHandler<DateTime> TimeAdjusted;

        DateTime SetCurrentTime(DateTime dateTime);
    }

    public class TimeProvider : ITimeProvider
    {
        public DateTime Now => DateTime.Now;
    }

    public class TestingTimeProvider : ITestingTimeProvider
    {
        private readonly object _lock = new object();

        public event EventHandler<DateTime> TimeAdjusted;

        public static ITestingTimeProvider Current => (ITestingTimeProvider)Utilities.TimeProvider;

        public DateTime Now { get; private set; } = DateTime.Now;

        public DateTime SetCurrentTime(DateTime dateTime)
        {
            lock (_lock)
            {
                if (dateTime < Now)
                {
                    throw new ArgumentOutOfRangeException(nameof(dateTime));
                }

                if (dateTime == Now)
                {
                    return Now;
                }

                Now = dateTime;
                TimeAdjusted?.Invoke(this, Now);
                return Now;
            }
        }
    }
}
