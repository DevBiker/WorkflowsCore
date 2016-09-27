using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkflowsCore.MonteCarlo
{
    public class SimulationsStats
    {
        public SimulationsStats(int randomGeneratorSeed, IList<EventMonitor> eventMonitors)
        {
            RandomGeneratorSeed = randomGeneratorSeed;
            NumberOfSimulations = eventMonitors.Count;
            AverageSimulationDuration =
                TimeSpan.FromSeconds(eventMonitors.Average(m => m.GetSimulationDuration().TotalSeconds));
            TotalNumberOfEvents = eventMonitors.Sum(m => m.GetEvents().Count);
            AverageNumberOfEventsPerSimulation = eventMonitors.Average(m => m.GetEvents().Count);
            Events =
                eventMonitors.SelectMany(m => m.GetEvents())
                    .GroupBy(e => e.Name)
                    .Select(g => Tuple.Create(g.Key, g.Count()))
                    .OrderByDescending(t => t.Item2)
                    .ToList();
        }

        public int RandomGeneratorSeed { get; }

        public int NumberOfSimulations { get; }

        public TimeSpan AverageSimulationDuration { get; }

        public int TotalNumberOfEvents { get; }

        public double AverageNumberOfEventsPerSimulation { get; }

        public IReadOnlyList<Tuple<string, int>> Events { get; } 

        public override string ToString()
        {
            var nl = Environment.NewLine;
            return string.Join(
                nl,
                $"Random number generator seed: {RandomGeneratorSeed}",
                $"Number of simulations: {NumberOfSimulations}",
                $"Average simulation duration: {AverageSimulationDuration}",
                $"Total events: {TotalNumberOfEvents}",
                $"Average number of events per simulation: {AverageNumberOfEventsPerSimulation}",
                string.Empty,
                "Events: ",
                string.Join(nl, Events.Select(t => $"{t.Item1}: {t.Item2}")));
        }
    }
}
