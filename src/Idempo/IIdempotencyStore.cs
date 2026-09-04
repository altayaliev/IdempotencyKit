namespace Idempo;

public enum IdempotencyReservationKind
{
    /// <summary>No prior record existed (or it expired) — the caller now owns this key and must execute the operation.</summary>
    Reserved,

    /// <summary>A request with the same key and fingerprint is still being processed elsewhere.</summary>
    InProgress,

    /// <summary>A request with the same key and fingerprint already finished — replay its stored result.</summary>
    Completed,

    /// <summary>The key was already used for a request with a different fingerprint (method/path/body).</summary>
    Conflict
}

public sealed class IdempotencyReservationResult
{
    public required IdempotencyReservationKind Kind { get; init; }

    public IdempotencyRecord? ExistingRecord { get; init; }

    public static IdempotencyReservationResult Reserved { get; } = new() { Kind = IdempotencyReservationKind.Reserved };

    public static IdempotencyReservationResult InProgress(IdempotencyRecord record) =>
        new() { Kind = IdempotencyReservationKind.InProgress, ExistingRecord = record };

    public static IdempotencyReservationResult Completed(IdempotencyRecord record) =>
        new() { Kind = IdempotencyReservationKind.Completed, ExistingRecord = record };

    public static IdempotencyReservationResult Conflict(IdempotencyRecord record) =>
        new() { Kind = IdempotencyReservationKind.Conflict, ExistingRecord = record };
}

/// <summary>
/// Stores idempotency reservations and their results. All atomicity guarantees for
/// concurrent duplicate requests live behind <see cref="TryReserveAsync"/> — implementations
/// must make that call race-safe using whatever atomic primitive their backend offers
/// (e.g. an in-memory compare-and-set, Redis SET NX, or a SQL unique constraint).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically attempts to claim <paramref name="key"/> for a new operation.
    /// </summary>
    Task<IdempotencyReservationResult> TryReserveAsync(
        string key,
        string fingerprintHash,
        TimeSpan pendingTimeout,
        TimeSpan completedTtl,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a previously reserved key as completed, storing its result for replay.</summary>
    Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a reservation without completing it — used when the operation itself failed
    /// (e.g. an unhandled exception), so a legitimate retry with the same key is not blocked
    /// until <c>pendingTimeout</c> elapses.
    /// </summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);
}
