using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Idempo.EntityFrameworkCore;

/// <summary>
/// Shared idempotency store backed by any EF Core relational provider (SQL Server, PostgreSQL,
/// SQLite, MySQL, ...). Safe to run behind multiple application instances, since every instance
/// reads and writes the same table. Requires <c>TContext</c> to be registered as an
/// <see cref="IDbContextFactory{TContext}"/> (via <c>services.AddDbContextFactory&lt;TContext&gt;(...)</c>)
/// and its model to include <see cref="IdempotencyModelBuilderExtensions.ConfigureIdempotencyStore"/>.
/// </summary>
/// <remarks>
/// A fresh, short-lived <see cref="DbContext"/> is created per operation via the factory rather
/// than injecting one directly — <see cref="IIdempotencyStore"/> is registered as a singleton,
/// and a normally-scoped <see cref="DbContext"/> is not safe to capture at that lifetime.
/// </remarks>
public sealed class EfCoreIdempotencyStore<TContext> : IIdempotencyStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    public EfCoreIdempotencyStore(IDbContextFactory<TContext> contextFactory, TimeProvider? timeProvider = null)
    {
        _contextFactory = contextFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IdempotencyReservationResult> TryReserveAsync(
        string key,
        string fingerprintHash,
        TimeSpan pendingTimeout,
        TimeSpan completedTtl,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        while (true)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var set = context.Set<IdempotencyRecordEntity>();

            // Tracked (not AsNoTracking) so an expired row can be removed below via a normal
            // SaveChangesAsync — EF adds the ExpiresAtUnixMs concurrency token to the DELETE's
            // WHERE clause automatically, which is more portable across providers than expressing
            // the same "delete only if still expired" guard through ExecuteDeleteAsync's predicate.
            var existing = await set.FirstOrDefaultAsync(r => r.Key == key, cancellationToken);

            if (existing is null)
            {
                set.Add(ToEntity(NewPendingRecord(key, fingerprintHash, now, pendingTimeout)));

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return IdempotencyReservationResult.Reserved;
                }
                catch (DbUpdateException)
                {
                    continue; // another instance/request inserted the same key concurrently
                }
            }

            if (existing.ExpiresAtUnixMs <= now.ToUnixTimeMilliseconds())
            {
                // Reclaiming is a delete-then-insert rather than an in-place update: it reuses the
                // exact same race-safe insert path below (a unique-constraint violation means
                // someone else reclaimed it first).
                context.Remove(existing);

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Someone else already changed or removed this row since we read it.
                }

                continue; // re-evaluate from scratch
            }

            if (!string.Equals(existing.FingerprintHash, fingerprintHash, StringComparison.Ordinal))
            {
                return IdempotencyReservationResult.Conflict(ToRecord(existing));
            }

            return existing.Status == IdempotencyRecordStatus.Completed
                ? IdempotencyReservationResult.Completed(ToRecord(existing))
                : IdempotencyReservationResult.InProgress(ToRecord(existing));
        }
    }

    public async Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        var entity = ToEntity(record);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Set<IdempotencyRecordEntity>()
            .Where(r => r.Key == key && r.Status == IdempotencyRecordStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, entity.Status)
                .SetProperty(r => r.StatusCode, entity.StatusCode)
                .SetProperty(r => r.ResponseBody, entity.ResponseBody)
                .SetProperty(r => r.ContentType, entity.ContentType)
                .SetProperty(r => r.HeadersJson, entity.HeadersJson)
                .SetProperty(r => r.ExpiresAtUnixMs, entity.ExpiresAtUnixMs),
                cancellationToken);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Set<IdempotencyRecordEntity>()
            .Where(r => r.Key == key && r.Status == IdempotencyRecordStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var nowUnixMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        // Tracked (not AsNoTracking), then removed one at a time via SaveChangesAsync rather than
        // a single ExecuteDeleteAsync(Key IN (...)): a blind bulk delete-by-key could wipe out a
        // brand new, non-expired reservation that a legitimate request creates for the same key
        // between this method's read and its delete. Going through the ExpiresAtUnixMs
        // concurrency token (see ConfigureIdempotencyStore) makes each row's delete a no-op if it
        // changed since we read it, at the cost of one round trip per expired row — an acceptable
        // trade-off for a periodic maintenance pass rather than a request-path operation.
        var expired = await context.Set<IdempotencyRecordEntity>()
            .Where(r => r.ExpiresAtUnixMs <= nowUnixMs)
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var entity in expired)
        {
            context.Remove(entity);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                removed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                context.Entry(entity).State = EntityState.Detached;
            }
        }

        return removed;
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

    private static IdempotencyRecordEntity ToEntity(IdempotencyRecord record) => new()
    {
        Key = record.Key,
        FingerprintHash = record.FingerprintHash,
        Status = record.Status,
        StatusCode = record.StatusCode,
        ResponseBody = record.ResponseBody,
        ContentType = record.ContentType,
        HeadersJson = record.Headers is null ? null : JsonSerializer.Serialize(record.Headers, IdempoJsonContext.Default.IReadOnlyDictionaryStringStringArray),
        CreatedAt = record.CreatedAt,
        ExpiresAtUnixMs = record.ExpiresAt.ToUnixTimeMilliseconds()
    };

    private static IdempotencyRecord ToRecord(IdempotencyRecordEntity entity) => new()
    {
        Key = entity.Key,
        FingerprintHash = entity.FingerprintHash,
        Status = entity.Status,
        StatusCode = entity.StatusCode,
        ResponseBody = entity.ResponseBody,
        ContentType = entity.ContentType,
        Headers = entity.HeadersJson is null
            ? null
            : JsonSerializer.Deserialize(entity.HeadersJson, IdempoJsonContext.Default.IReadOnlyDictionaryStringStringArray),
        CreatedAt = entity.CreatedAt,
        ExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.ExpiresAtUnixMs)
    };
}
