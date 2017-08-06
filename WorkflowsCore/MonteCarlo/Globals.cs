using System.Threading;
using WorkflowsCore.Time;

namespace WorkflowsCore.MonteCarlo
{
    public static class Globals
    {
        public const string DefaultDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        private static readonly AsyncLocal<RandomGenerator> AsyncRandomGenerator = new AsyncLocal<RandomGenerator>();
        private static readonly AsyncLocal<EventMonitor> AsynEventMonitor = new AsyncLocal<EventMonitor>();
        private static readonly AsyncLocal<int> AsynEventId = new AsyncLocal<int>();

        public static RandomGenerator RandomGenerator
        {
            get { return AsyncRandomGenerator.Value; }
            internal set { AsyncRandomGenerator.Value = value; }
        }

        public static EventMonitor EventMonitor
        {
            get { return AsynEventMonitor.Value; }
            internal set { AsynEventMonitor.Value = value; }
        }

        public static int EventId
        {
            get { return AsynEventId.Value; }
            internal set { AsynEventId.Value = value; }
        }

        public static ISystemClock SystemClock => Utilities.SystemClock;
    }
}
