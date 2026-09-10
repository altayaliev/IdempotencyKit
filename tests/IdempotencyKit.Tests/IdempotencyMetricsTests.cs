using System.Diagnostics.Metrics;
using IdempotencyKit.Diagnostics;

namespace IdempotencyKit.Tests;

public class IdempotencyMetricsTests
{
    [Fact]
    public void RecordReservation_emits_a_counter_measurement_tagged_with_the_outcome()
    {
        using var metrics = new IdempotencyMetrics();
        var measurements = new List<(long Value, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == IdempotencyMetrics.MeterName && instrument.Name == "idempotencykit.reservations")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var outcome = tags.ToArray().FirstOrDefault(t => t.Key == "idempotencykit.outcome").Value?.ToString();
            measurements.Add((value, outcome));
        });
        listener.Start();

        metrics.RecordReservation(IdempotencyReservationKind.Reserved);
        metrics.RecordReservation(IdempotencyReservationKind.Conflict);
        metrics.RecordReservation(IdempotencyReservationKind.Conflict);

        Assert.Equal(3, measurements.Count);
        Assert.Equal(1, measurements.Count(m => m.Outcome == "reserved"));
        Assert.Equal(2, measurements.Count(m => m.Outcome == "conflict"));
    }

    [Fact]
    public void RecordPurged_emits_the_count_but_skips_zero()
    {
        using var metrics = new IdempotencyMetrics();
        var total = 0L;
        var callCount = 0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == IdempotencyMetrics.MeterName && instrument.Name == "idempotencykit.cleanup.purged")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            total += value;
            callCount++;
        });
        listener.Start();

        metrics.RecordPurged(0);  // must not emit
        metrics.RecordPurged(5);
        metrics.RecordPurged(3);

        Assert.Equal(2, callCount);
        Assert.Equal(8, total);
    }
}
