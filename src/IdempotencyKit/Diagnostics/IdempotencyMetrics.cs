using System.Diagnostics.Metrics;

namespace IdempotencyKit.Diagnostics;

/// <summary>
/// IdempotencyKit's <see cref="System.Diagnostics.Metrics.Meter"/>, named <see cref="MeterName"/>. Any
/// OpenTelemetry (or other <c>System.Diagnostics.Metrics</c> listener) setup can observe it by
/// calling <c>.AddMeter("IdempotencyKit")</c> — no OpenTelemetry package dependency is added by IdempotencyKit
/// itself. Registered as a singleton by <c>AddIdempotencyKit()</c>.
/// </summary>
public sealed class IdempotencyMetrics : IDisposable
{
    public const string MeterName = "IdempotencyKit";

    private readonly Meter _meter;
    private readonly Counter<long> _reservations;
    private readonly Counter<long> _purged;
    private readonly Counter<long> _completeFailures;

    public IdempotencyMetrics()
    {
        _meter = new Meter(MeterName);
        _reservations = _meter.CreateCounter<long>(
            "idempotencykit.reservations",
            unit: "{reservation}",
            description: "Idempotency reservation attempts, tagged by outcome (reserved, in_progress, completed, conflict).");
        _purged = _meter.CreateCounter<long>(
            "idempotencykit.cleanup.purged",
            unit: "{record}",
            description: "Expired idempotency records removed by the background cleanup service.");
        _completeFailures = _meter.CreateCounter<long>(
            "idempotencykit.complete_failures",
            unit: "{failure}",
            description: "Times the handler succeeded but persisting its result to the store failed. The caller " +
                          "still receives the real response, but a retry after PendingTimeout may re-run the handler.");
    }

    public void RecordReservation(IdempotencyReservationKind kind) =>
        _reservations.Add(1, new KeyValuePair<string, object?>("idempotencykit.outcome", OutcomeTag(kind)));

    public void RecordPurged(int count)
    {
        if (count > 0)
        {
            _purged.Add(count);
        }
    }

    public void RecordCompleteFailure() => _completeFailures.Add(1);

    private static string OutcomeTag(IdempotencyReservationKind kind) => kind switch
    {
        IdempotencyReservationKind.Reserved => "reserved",
        IdempotencyReservationKind.InProgress => "in_progress",
        IdempotencyReservationKind.Completed => "completed",
        IdempotencyReservationKind.Conflict => "conflict",
        _ => "unknown"
    };

    public void Dispose() => _meter.Dispose();
}
