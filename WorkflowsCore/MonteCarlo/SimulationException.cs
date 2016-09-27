using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkflowsCore.MonteCarlo
{
    public class SimulationException : Exception
    {
        private readonly int _randomNumberGeneratorSeed;

        public SimulationException(
            int randomNumberGeneratorSeed,
            IEnumerable<Event> events,
            Exception innerException = null)
            : base(null, innerException)
        {
            _randomNumberGeneratorSeed = randomNumberGeneratorSeed;
            Events = events.ToList();
        }

        public IReadOnlyList<Event> Events { get; }

        public override string Message
        {
            get
            {
                var nl = Environment.NewLine;
                return string.Join(
                    nl,
                    $"{nl}Random number generator seed: {_randomNumberGeneratorSeed}{nl}",
                    $"Sequence of events that leads to exception: {nl}{string.Join(nl, Events)}{nl}");
            }
        }
    }
}
