using IdempotencyKit.TestKit;
using Microsoft.Extensions.Time.Testing;

namespace IdempotencyKit.Tests;

public class InMemoryIdempotencyStoreTests : IdempotencyStoreContractTests
{
    protected override IIdempotencyStore CreateStore(TimeProvider timeProvider) => new InMemoryIdempotencyStore(timeProvider);

    [Fact]
    public async Task PurgeExpiredAsync_removes_only_expired_entries_and_reports_the_count()
    {
        var timeProvider = new FakeTimeProvider();
        var store = new InMemoryIdempotencyStore(timeProvider);

        await store.TryReserveAsync("expires-soon-1", "fp", PendingTimeout, CompletedTtl);
        await store.TryReserveAsync("expires-soon-2", "fp", PendingTimeout, CompletedTtl);

        var beforeExpiry = await store.PurgeExpiredAsync();
        Assert.Equal(0, beforeExpiry);

        timeProvider.Advance(PendingTimeout + TimeSpan.FromSeconds(1));
        await store.TryReserveAsync("still-fresh", "fp", PendingTimeout, CompletedTtl);

        var afterExpiry = await store.PurgeExpiredAsync();

        Assert.Equal(2, afterExpiry);
    }
}
