using IdempotencyKit.TestKit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdempotencyKit.EntityFrameworkCore.Tests;

/// <summary>
/// Runs the full <see cref="IdempotencyStoreContractTests"/> suite against
/// <see cref="EfCoreIdempotencyStore{TContext}"/> using a real relational engine (SQLite) so
/// SQL-level behavior — unique-constraint conflicts, <c>ExecuteUpdateAsync</c> translation — is
/// exercised, not just LINQ-to-objects. Each test gets its own isolated, named, shared-cache
/// in-memory SQLite database: isolated so tests can't interfere with each other, shared-cache so
/// the concurrency test's many parallel <see cref="IDbContextFactory{TContext}"/>-created
/// connections all see the same database (a single open <see cref="SqliteConnection"/> object
/// cannot itself be shared across concurrent commands).
/// </summary>
public sealed class EfCoreIdempotencyStoreTests : IdempotencyStoreContractTests, IDisposable
{
    private readonly List<SqliteConnection> _keepAliveConnections = new();
    private readonly List<ServiceProvider> _providers = new();

    protected override IIdempotencyStore CreateStore(TimeProvider timeProvider)
    {
        var connectionString = $"Data Source=idempotencykit-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        // Closing the last connection to a shared-cache in-memory database destroys it, so one
        // connection is kept open for the lifetime of the test purely to keep the database alive.
        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        _keepAliveConnections.Add(keepAlive);

        var services = new ServiceCollection();
        services.AddDbContextFactory<TestDbContext>(options => options.UseSqlite(connectionString));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var factory = provider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using (var context = factory.CreateDbContext())
        {
            context.Database.EnsureCreated();
        }

        return new EfCoreIdempotencyStore<TestDbContext>(factory, timeProvider);
    }

    [Fact]
    public async Task Response_headers_round_trip_through_json_storage()
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
        var headers = result.ExistingRecord!.Headers!;
        Assert.Equal(["abc-123"], headers["X-Trace-Id"]);
        Assert.Equal(["\"v1\"", "\"v2\""], headers["ETag"]);
    }

    [Fact]
    public async Task PurgeExpiredAsync_deletes_expired_rows_and_reports_the_count()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var store = CreateStore(timeProvider);

        await store.TryReserveAsync(UniqueKey(), "fp", PendingTimeout, CompletedTtl);
        await store.TryReserveAsync(UniqueKey(), "fp", PendingTimeout, CompletedTtl);

        Assert.Equal(0, await store.PurgeExpiredAsync());

        timeProvider.Advance(PendingTimeout + TimeSpan.FromSeconds(1));
        await store.TryReserveAsync(UniqueKey(), "fp", PendingTimeout, CompletedTtl); // stays fresh relative to the advanced clock

        Assert.Equal(2, await store.PurgeExpiredAsync());
        Assert.Equal(0, await store.PurgeExpiredAsync()); // idempotent — nothing left to purge
    }

    [Fact]
    public async Task PurgeExpiredAsync_removes_more_than_one_page_worth_of_expired_rows()
    {
        // PurgeExpiredAsync reads and deletes in pages of 500 internally; this exercises the
        // multi-page loop rather than the single-round-trip common case already covered above.
        const int RowCount = 550;
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var store = CreateStore(timeProvider);

        for (var i = 0; i < RowCount; i++)
        {
            await store.TryReserveAsync($"{UniqueKey()}-{i}", "fp", PendingTimeout, CompletedTtl);
        }

        timeProvider.Advance(PendingTimeout + TimeSpan.FromSeconds(1));

        Assert.Equal(RowCount, await store.PurgeExpiredAsync());
        Assert.Equal(0, await store.PurgeExpiredAsync());
    }

    [Fact]
    public async Task Reservation_survives_across_separate_DbContext_instances()
    {
        // Simulates two different application instances (or two requests handled by different
        // pooled DbContexts) observing the same row through the shared database.
        var store = CreateStore(TimeProvider.System);
        var key = UniqueKey();

        var first = await store.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);
        var second = await store.TryReserveAsync(key, "fp", PendingTimeout, CompletedTtl);

        Assert.Equal(IdempotencyReservationKind.Reserved, first.Kind);
        Assert.Equal(IdempotencyReservationKind.InProgress, second.Kind);
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        foreach (var connection in _keepAliveConnections)
        {
            connection.Dispose();
        }
    }
}
