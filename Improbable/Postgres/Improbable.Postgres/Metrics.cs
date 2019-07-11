using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Improbable.Postgres
{
    public static class Metrics
    {
        private const int MaxSamples = 100;

        private const string Database = "Db.";

        // TODO: Implement? Or do this externally
        public const string MaxConcurrentConnections = Database + nameof(MaxConcurrentConnections);
        public const string ConcurrentConnections = Database + nameof(ConcurrentConnections);
        public const string ConnectionDuration = Database + nameof(ConnectionDuration);
        public const string TotalChangesReceived = Database + nameof(TotalChangesReceived);
        public const string RoundTripDuration = Database + nameof(RoundTripDuration);

        private static readonly ConcurrentDictionary<string, long> Counters = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long[]> Timings = new ConcurrentDictionary<string, long[]>();
        private static readonly ConcurrentDictionary<string, int> TimingPointers = new ConcurrentDictionary<string, int>();

        public static void Inc(string metricName)
        {
            Counters.AddOrUpdate(metricName, key => 1, (key, oldCount) => oldCount + 1);
        }

        public static void Dec(string metricName)
        {
            Counters.AddOrUpdate(metricName, key => 1, (key, oldCount) => oldCount - 1);
        }

        public static void Observe(string metricName, long time)
        {
            Timings.AddOrUpdate(metricName, key =>
            {
                var samples = new long[MaxSamples];
                samples.Initialize();
                samples[0] = time;
                return samples;
            }, (key, samples) =>
            {
                var nextSlot = TimingPointers.GetOrAdd(metricName, 1);
                samples[nextSlot] = time;
                TimingPointers[metricName] = (nextSlot + 1) % MaxSamples;
                return samples;
            });
        }

        public static Dictionary<string, long> GetCounts()
        {
            return Counters.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public static Dictionary<string, long> GetTimingStats()
        {
            // Currently the avg and the max. Any aggregation could happen here (or dump the raw values and an external system can do this)
            var averages = Timings.ToDictionary(kv => PrefixBaseName(kv.Key, "Avg"), kv => (long) kv.Value.Average());
            var max = Timings.ToDictionary(kv => PrefixBaseName(kv.Key, "Max"), kv => kv.Value.Max());
            return averages.Concat(max).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static string PrefixBaseName(string oldName, string prefix)
        {
            var pieces = oldName.Split('.');
            if (pieces.Length < 2)
            {
                return prefix + pieces;
            }

            return pieces[0] + "." + prefix + pieces[1];
        }
    }
}
