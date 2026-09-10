namespace IdempotencyKit.AspNetCore.Tests;

/// <summary>Wraps a real store but can be told to fail its next <see cref="CompleteAsync"/> call once.</summary>
internal sealed class CompleteFailingStore : IIdempotencyStore
{
    private readonly IIdempotencyStore _inner;

    public CompleteFailingStore(IIdempotencyStore inner) => _inner = inner;

    public bool FailNextComplete { get; set; }

    public Task<IdempotencyReservationResult> TryReserveAsync(
        string key, string fingerprintHash, TimeSpan pendingTimeout, TimeSpan completedTtl, CancellationToken cancellationToken = default) =>
        _inner.TryReserveAsync(key, fingerprintHash, pendingTimeout, completedTtl, cancellationToken);

    public Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        if (FailNextComplete)
        {
            FailNextComplete = false;
            throw new InvalidOperationException("Simulated store failure while persisting the result.");
        }

        return _inner.CompleteAsync(key, record, cancellationToken);
    }

    public Task ReleaseAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.ReleaseAsync(key, cancellationToken);

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) =>
        _inner.PurgeExpiredAsync(cancellationToken);
}
