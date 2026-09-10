using IdempotencyKit.TestKit;
using StackExchange.Redis;

namespace IdempotencyKit.StackExchangeRedis.Tests;

/// <summary>
/// Runs the full <see cref="IdempotencyStoreContractTests"/> suite against
/// <see cref="RedisIdempotencyStore"/> using a real Redis server, so the Lua scripts' actual
/// atomicity — not just their logic on paper — is exercised. Requires a Redis instance reachable
/// at the connection string in <see cref="RedisConnectionString"/> (override via the
/// <c>IDEMPOTENCYKIT_TEST_REDIS</c> environment variable); see the repo README for how to start one
/// locally with Docker.
/// </summary>
public sealed class RedisIdempotencyStoreTests : IdempotencyStoreContractTests, IDisposable
{
    private static readonly string RedisConnectionString =
        Environment.GetEnvironmentVariable("IDEMPOTENCYKIT_TEST_REDIS") ?? "localhost:16379";

    // Fixed per test-class instance (xUnit creates one instance per test method), so different
    // stores built within the same test method share a prefix while still being isolated from
    // every other test method and run.
    private readonly string _keyPrefix = $"idempotencykit-tests:{Guid.NewGuid():N}:";
    private readonly List<IConnectionMultiplexer> _connections = new();

    protected override IIdempotencyStore CreateStore(TimeProvider timeProvider)
    {
        var connection = ConnectionMultiplexer.Connect(RedisConnectionString);
        _connections.Add(connection);
        return new RedisIdempotencyStore(connection, timeProvider, _keyPrefix);
    }

    [Fact]
    public async Task Response_headers_and_body_round_trip_through_redis()
    {
        var store = CreateStore(TimeProvider.System);
        var key = UniqueKey();
        const string fingerprint = "fp";

        await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);
        await store.CompleteAsync(key, new IdempotencyRecord
        {
            Key = key,
            FingerprintHash = fingerprint,
            Status = IdempotencyRecordStatus.Completed,
            StatusCode = 201,
            ContentType = "application/json",
            Headers = new Dictionary<string, string[]> { ["X-Trace-Id"] = ["abc-123"], ["ETag"] = ["\"v1\"", "\"v2\""] },
            ResponseBody = "{\"ok\":true}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + CompletedTtl
        });

        var result = await store.TryReserveAsync(key, fingerprint, PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Completed, result.Kind);
        var record = result.ExistingRecord!;
        Assert.Equal(201, record.StatusCode);
        Assert.Equal("{\"ok\":true}"u8.ToArray(), record.ResponseBody);
        Assert.Equal(["abc-123"], record.Headers!["X-Trace-Id"]);
        Assert.Equal(["\"v1\"", "\"v2\""], record.Headers!["ETag"]);
    }

    [Fact]
    public async Task Reservation_is_visible_through_a_separate_connection()
    {
        // Simulates two different application instances, each with their own Redis connection,
        // observing the same key.
        var storeA = CreateStore(TimeProvider.System);
        var storeB = CreateStore(TimeProvider.System); // separate connection, same _keyPrefix
        var key = UniqueKey();

        var first = await storeA.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);
        var second = await storeB.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Reserved, first.Kind);
        Assert.Equal(IdempotencyReservationKind.InProgress, second.Kind);
    }

    [Fact]
    public async Task PurgeExpiredAsync_is_a_safe_no_op_because_redis_expires_keys_natively()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var store = CreateStore(timeProvider);

        await store.TryReserveAsync(UniqueKey(), "fp", PendingTimeout, CompletedTtl);
        timeProvider.Advance(PendingTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(0, await store.PurgeExpiredAsync());
    }

    public void Dispose()
    {
        foreach (var connection in _connections)
        {
            connection.Dispose();
        }
    }
}
