using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using Forum.Common.Telemetry;

namespace Forum.TestUtilities;

/// <summary>
/// Captures measurements from ONE <see cref="ForumMetrics"/> instance's Meter, matched by the
/// <see cref="IMeterFactory"/> scope that created it — so parallel test classes never observe each other's counters.
/// </summary>
public sealed class MetricCapture : IDisposable
{
    public sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<Measurement> _measurements = new();

    public MetricCapture(object meterScope)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == ForumMetrics.MeterName
                && ReferenceEquals(instrument.Meter.Scope, meterScope))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public IReadOnlyList<Measurement> For(string instrument) =>
        _measurements.Where(measurement => measurement.Instrument == instrument).ToList();

    public double Total(string instrument) => For(instrument).Sum(static measurement => measurement.Value);

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copied = new Dictionary<string, object?>(tags.Length);
        foreach (var tag in tags)
        {
            copied[tag.Key] = tag.Value;
        }

        _measurements.Enqueue(new Measurement(instrument.Name, value, copied));
    }
}
