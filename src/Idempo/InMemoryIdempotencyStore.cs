using System.Collections.Concurrent;

namespace Idempo;

/// <summary>
/// Single-process idempotency store backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Suitable for development, testing, or single-instance deployments. Multi-instance deployments
/// need a shared backend (e.g. a future Redis or EF Core store) so all instances see the same reservations.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new();
    private readonly TimeProvider _timeProvider;

    public InMemoryIdempotencyStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IdempotencyReservationResult> TryReserveAsync(
        string key,
        string fingerprintHash,
        TimeSpan pendingTimeout,
        TimeSpan completedTtl,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        while (true)
        {
            if (_records.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAt <= now)
                {
                    // Expired — either a stuck Pending reservation (e.g. the owning request crashed)
                    // or a Completed record past its replay TTL. Either way, it is free to reclaim.
                    var fresh = NewPendingRecord(key, fingerprintHash, now, pendingTimeout);
                    if (_records.TryUpdate(key, fresh, existing))
                    {
                        return Task.FromResult(IdempotencyReservationResult.Reserved);
                    }

                    continue; // lost a race with another reserve/complete — re-evaluate current state
                }

                if (!string.Equals(existing.FingerprintHash, fingerprintHash, StringComparison.Ordinal))
                {
                    return Task.FromResult(IdempotencyReservationResult.Conflict(existing));
                }

                return Task.FromResult(existing.Status == IdempotencyRecordStatus.Completed
                    ? IdempotencyReservationResult.Completed(existing)
                    : IdempotencyReservationResult.InProgress(existing));
            }

            var record = NewPendingRecord(key, fingerprintHash, now, pendingTimeout);
            if (_records.TryAdd(key, record))
            {
                return Task.FromResult(IdempotencyReservationResult.Reserved);
            }

            // Another request just created a reservation for this key — loop and evaluate it.
        }
    }

    public Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        // Only overwrite a reservation this call is still the rightful owner of. If the pending
        // reservation already expired and was reclaimed by someone else, drop this result on the
        // floor rather than clobbering their in-flight reservation.
        if (_records.TryGetValue(key, out var current) && current.Status == IdempotencyRecordStatus.Pending)
        {
            _records.TryUpdate(key, record, current);
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_records.TryGetValue(key, out var current) && current.Status == IdempotencyRecordStatus.Pending)
        {
            _records.TryRemove(new KeyValuePair<string, IdempotencyRecord>(key, current));
        }

        return Task.CompletedTask;
    }

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var removed = 0;

        foreach (var entry in _records)
        {
            if (entry.Value.ExpiresAt <= now && _records.TryRemove(entry))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    private static IdempotencyRecord NewPendingRecord(string key, string fingerprintHash, DateTimeOffset now, TimeSpan pendingTimeout) =>
        new()
        {
            Key = key,
            FingerprintHash = fingerprintHash,
            Status = IdempotencyRecordStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now + pendingTimeout
        };
}
