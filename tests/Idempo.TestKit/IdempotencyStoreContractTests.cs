using Microsoft.Extensions.Time.Testing;

namespace Idempo.TestKit;

/// <summary>
/// Behavioral contract every <see cref="IIdempotencyStore"/> implementation must satisfy,
/// regardless of backend. Each concrete store's test project derives from this class and
/// implements <see cref="CreateStore"/> — the rest runs automatically, so InMemory, EF Core,
/// Redis, or any future backend are all held to the exact same guarantees.
/// </summary>
public abstract class IdempotencyStoreContractTests
{
    protected static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(60);
    protected static readonly TimeSpan CompletedTtl = TimeSpan.FromHours(24);

    /// <summary>Creates a fresh, empty store backed by the given clock.</summary>
    protected abstract IIdempotencyStore CreateStore(TimeProvider timeProvider);

    private IIdempotencyStore CreateStore() => CreateStore(TimeProvider.System);

    [Fact]
    public async Task Concurrent_reservations_for_the_same_key_yield_exactly_one_winner()
    {
        var store = CreateStore();
        var key = UniqueKey();

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ =>
            store.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl)));

        Assert.Single(results, r => r.Kind == IdempotencyReservationKind.Reserved);
        Assert.Equal(24, results.Count(r => r.Kind == IdempotencyReservationKind.InProgress));
    }

    [Fact]
    public async Task Completed_reservation_replays_for_matching_fingerprint()
    {
        var store = CreateStore();
        var key = UniqueKey();
        const string fingerprint = "fp";

        var first = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        Assert.Equal(IdempotencyReservationKind.Reserved, first.Kind);

        await store.CompleteAsync(key, new IdempotencyRecord
        {
            Key = key,
            FingerprintHash = fingerprint,
            Status = IdempotencyRecordStatus.Completed,
            StatusCode = 201,
            ResponseBody = "hello"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + CompletedTtl
        });

        var second = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Completed, second.Kind);
        Assert.Equal(201, second.ExistingRecord!.StatusCode);
        Assert.Equal("hello"u8.ToArray(), second.ExistingRecord!.ResponseBody);
    }

    [Fact]
    public async Task Same_key_with_different_fingerprint_is_a_conflict()
    {
        var store = CreateStore();
        var key = UniqueKey();

        await store.TryReserveAsync(key, "fp-a", PendingTimeout, CompletedTtl);
        var second = await store.TryReserveAsync(key, "fp-b", PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Conflict, second.Kind);
    }

    [Fact]
    public async Task Releasing_a_reservation_frees_the_key_for_retry()
    {
        var store = CreateStore();
        var key = UniqueKey();

        await store.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);
        await store.ReleaseAsync(key);

        var retry = await store.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Reserved, retry.Kind);
    }

    [Fact]
    public async Task Releasing_a_completed_reservation_does_not_erase_it()
    {
        // A caller should only ever release a reservation it still Pending-ly owns (e.g. its
        // handler threw). Calling Release after Complete must be a safe no-op, not data loss.
        var store = CreateStore();
        var key = UniqueKey();
        const string fingerprint = "fp";

        await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        await store.CompleteAsync(key, new IdempotencyRecord
        {
            Key = key,
            FingerprintHash = fingerprint,
            Status = IdempotencyRecordStatus.Completed,
            StatusCode = 200,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + CompletedTtl
        });

        await store.ReleaseAsync(key);

        var afterRelease = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        Assert.Equal(IdempotencyReservationKind.Completed, afterRelease.Kind);
    }

    [Fact]
    public async Task Abandoned_pending_reservation_is_reclaimed_after_pending_timeout()
    {
        var timeProvider = new FakeTimeProvider();
        var store = CreateStore(timeProvider);
        var key = UniqueKey();

        var first = await store.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);
        Assert.Equal(IdempotencyReservationKind.Reserved, first.Kind);

        timeProvider.Advance(PendingTimeout + TimeSpan.FromSeconds(1));

        var reclaimed = await store.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Reserved, reclaimed.Kind);
    }

    [Fact]
    public async Task Completed_record_stops_replaying_after_completed_ttl_elapses()
    {
        var timeProvider = new FakeTimeProvider();
        var store = CreateStore(timeProvider);
        var key = UniqueKey();
        const string fingerprint = "fp";

        await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        await store.CompleteAsync(key, new IdempotencyRecord
        {
            Key = key,
            FingerprintHash = fingerprint,
            Status = IdempotencyRecordStatus.Completed,
            StatusCode = 200,
            CreatedAt = timeProvider.GetUtcNow(),
            ExpiresAt = timeProvider.GetUtcNow() + CompletedTtl
        });

        timeProvider.Advance(CompletedTtl + TimeSpan.FromSeconds(1));

        var afterExpiry = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Reserved, afterExpiry.Kind);
    }

    [Fact]
    public async Task Different_keys_do_not_interfere_with_each_other()
    {
        var store = CreateStore();

        var a = await store.TryReserveAsync(UniqueKey(), "fp", PendingTimeout, CompletedTtl);
        var b = await store.TryReserveAsync(UniqueKey(), "fp", PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Reserved, a.Kind);
        Assert.Equal(IdempotencyReservationKind.Reserved, b.Kind);
    }

    [Fact]
    public async Task PurgeExpiredAsync_does_not_disturb_an_active_pending_reservation()
    {
        var store = CreateStore();
        var key = UniqueKey();

        await store.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);
        await store.PurgeExpiredAsync();

        var result = await store.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);
        Assert.Equal(IdempotencyReservationKind.InProgress, result.Kind);
    }

    [Fact]
    public async Task PurgeExpiredAsync_does_not_disturb_an_active_completed_record()
    {
        var store = CreateStore();
        var key = UniqueKey();
        const string fingerprint = "fp";

        await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        await store.CompleteAsync(key, new IdempotencyRecord
        {
            Key = key,
            FingerprintHash = fingerprint,
            Status = IdempotencyRecordStatus.Completed,
            StatusCode = 200,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + CompletedTtl
        });

        await store.PurgeExpiredAsync();

        var result = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        Assert.Equal(IdempotencyReservationKind.Completed, result.Kind);
    }

    /// <summary>
    /// A fresh key per test method so implementations backed by a shared, persistent store
    /// (a real Redis instance, a real database) don't see cross-test contamination.
    /// </summary>
    protected static string UniqueKey([System.Runtime.CompilerServices.CallerMemberName] string testName = "") =>
        $"{testName}-{Guid.NewGuid():N}";
}
