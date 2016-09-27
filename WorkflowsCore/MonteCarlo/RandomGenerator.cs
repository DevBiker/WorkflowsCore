using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkflowsCore.MonteCarlo
{
    public class RandomGenerator
    {
        private readonly Random _random;
        private readonly object _randomLock = new object();

        public RandomGenerator(int? seed)
        {
            Seed = seed ?? Environment.TickCount;
            _random = new Random(Seed);
        }

        public int Seed { get; }

        public int Next()
        {
            lock (_randomLock)
            {
                return _random.Next();
            }
        }

        public int Next(int maxValue)
        {
            lock (_randomLock)
            {
                return _random.Next(maxValue);
            }
        }

        public int Next(int minValue, int maxValue)
        {
            lock (_randomLock)
            {
                return _random.Next(minValue, maxValue);
            }
        }

        public double NextDouble()
        {
            lock (_randomLock)
            {
                return _random.NextDouble();
            }
        }

        public int GetRandomItemIndexFromProbabilityWeights(IList<double> probabilityWeights)
        {
            var total = probabilityWeights.Sum();
            var randomValue = NextDouble();
            var current = 0.0;
            return probabilityWeights.Select(
                (w, i) =>
                {
                    current += w / total;
                    return current >= randomValue ? i : -1;
                }).First(i => i >= 0);
        }
    }
}
