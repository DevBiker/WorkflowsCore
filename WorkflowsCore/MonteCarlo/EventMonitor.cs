using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace WorkflowsCore.MonteCarlo
{
    public struct Event
    {
        public Event(int eventId, string name, string parameters = null)
        {
            EventId = eventId;
            Name = name;
            Parameters = parameters;
            Time = DateTime.Now;
        }

        public DateTime Time { get; }

        public int EventId { get; set; }

        public string Name { get; }

        public string Parameters { get; }

        public override string ToString()
        {
            var parameters = string.IsNullOrWhiteSpace(Parameters) ? string.Empty : $": {Parameters}";
            return $"{Time.ToString("mm:ss.fff")}: [{EventId}] {Name}{parameters}";
        }
    }

    public class EventMonitor
    {
        private readonly IList<Event> _events = new List<Event>();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public void LogEvent(string eventName, string eventParameters = null)
        {
            lock (_events)
            {
                _events.Add(new Event(Globals.EventId, eventName, eventParameters));
            }
        }

        public void SimulationCompleted()
        {
            _stopwatch.Stop();
        }

        public IList<Event> GetEvents()
        {
            lock (_events)
            {
                return _events.ToList();
            }
        }

        public TimeSpan GetSimulationDuration() => _stopwatch.Elapsed;
    }
}
