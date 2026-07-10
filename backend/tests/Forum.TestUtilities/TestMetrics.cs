using System.Diagnostics.Metrics;

using Forum.Common.Telemetry;

namespace Forum.TestUtilities;

/// <summary>Builds isolated <see cref="ForumMetrics"/> instances for unit tests — one throwaway Meter per call, no exporter.</summary>
public static class TestMetrics
{
    public static ForumMetrics Create(TimeProvider? time = null) => Create(out _, time);

    /// <summary><paramref name="meterScope"/> identifies the created Meter for a <see cref="MetricCapture"/>.</summary>
    public static ForumMetrics Create(out object meterScope, TimeProvider? time = null)
    {
        var factory = new TestMeterFactory();
        meterScope = factory;
        return new ForumMetrics(factory, time ?? TimeProvider.System);
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
