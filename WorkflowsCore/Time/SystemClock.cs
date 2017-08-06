using System;

namespace WorkflowsCore.Time
{
    public interface ISystemClock
    {
        DateTime Now { get; }

        DateTimeOffset UtcNow { get; }
    }

    public interface ITestingSystemClock : ISystemClock
    {
        event EventHandler<DateTimeOffset> TimeAdjusted;

        DateTime SetCurrentTime(DateTime dateTime);
    }

    public class SystemClock : ISystemClock
    {
        public DateTime Now => DateTime.Now;

        public DateTimeOffset UtcNow => DateTimeOffset.Now;
    }

    public class TestingSystemClock : ITestingSystemClock
    {
        private readonly object _lock = new object();

        public event EventHandler<DateTimeOffset> TimeAdjusted;

        public static ITestingSystemClock Current => (ITestingSystemClock)Utilities.SystemClock;

        public DateTime Now { get; private set; } = DateTime.Now;

        public DateTimeOffset UtcNow => Now;

        public DateTime SetCurrentTime(DateTime dateTime)
        {
            if (dateTime.Kind != DateTimeKind.Local)
            {
                throw new ArgumentOutOfRangeException(nameof(dateTime));
            }

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
                TimeAdjusted?.Invoke(this, UtcNow);
                return Now;
            }
        }
    }
}
