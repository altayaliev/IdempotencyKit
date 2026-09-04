using Microsoft.Extensions.Time.Testing;

namespace Idempo.Tests;

public class InMemoryIdempotencyStoreTests
{
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CompletedTtl = TimeSpan.FromHours(24);

    [Fact]
    public async Task Concurrent_reservations_for_the_same_key_yield_exactly_one_winner()
    {
        var store = new InMemoryIdempotencyStore();
        const string key = "key-1";
        const string fingerprint = "fp-1";

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
            store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl)));

        Assert.Single(results, r => r.Kind == IdempotencyReservationKind.Reserved);
        Assert.Equal(49, results.Count(r => r.Kind == IdempotencyReservationKind.InProgress));
    }

    [Fact]
    public async Task Completed_reservation_replays_for_matching_fingerprint()
    {
        var store = new InMemoryIdempotencyStore();
        const string key = "key-2";
        const string fingerprint = "fp-2";

        var first = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        Assert.Equal(IdempotencyReservationKind.Reserved, first.Kind);

        var record = new IdempotencyRecord
        {
            Key = key,
            FingerprintHash = fingerprint,
            Status = IdempotencyRecordStatus.Completed,
            StatusCode = 201,
            ResponseBody = "hello"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + CompletedTtl
        };
        await store.CompleteAsync(key, record);

        var second = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Completed, second.Kind);
        Assert.Equal(201, second.ExistingRecord!.StatusCode);
        Assert.Equal("hello"u8.ToArray(), second.ExistingRecord!.ResponseBody);
    }

    [Fact]
    public async Task Same_key_with_different_fingerprint_is_a_conflict()
    {
        var store = new InMemoryIdempotencyStore();
        const string key = "key-3";

        await store.TryReserveAsync(key, "fp-a", PendingTimeout, CompletedTtl);
        var second = await store.TryReserveAsync(key, "fp-b", PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Conflict, second.Kind);
    }

    [Fact]
    public async Task Releasing_a_reservation_frees_the_key_for_retry()
    {
        var store = new InMemoryIdempotencyStore();
        const string key = "key-4";
        const string fingerprint = "fp-4";

        await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        await store.ReleaseAsync(key);

        var retry = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Reserved, retry.Kind);
    }

    [Fact]
    public async Task Abandoned_pending_reservation_is_reclaimed_after_pending_timeout()
    {
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        const string key = "key-5";
        const string fingerprint = "fp-5";

        var first = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        Assert.Equal(IdempotencyReservationKind.Reserved, first.Kind);

        // Simulate the owning request crashing without ever calling Complete/Release.
        timeProvider.Advance(PendingTimeout + TimeSpan.FromSeconds(1));

        var reclaimed = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Reserved, reclaimed.Kind);
    }

    [Fact]
    public async Task Completed_record_stops_replaying_after_completed_ttl_elapses()
    {
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);
        const string key = "key-6";
        const string fingerprint = "fp-6";

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
}
