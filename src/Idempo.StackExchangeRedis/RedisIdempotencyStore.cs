using System.Text.Json;
using StackExchange.Redis;

namespace Idempo.StackExchangeRedis;

/// <summary>
/// Shared idempotency store backed by Redis. Safe to run behind multiple application instances,
/// since every instance reads and writes the same Redis keys. All atomicity is provided by
/// Redis's own atomic Lua script execution — see <see cref="LuaScripts"/> — no external
/// distributed-lock library is required.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly TimeProvider _timeProvider;
    private readonly string _keyPrefix;

    /// <param name="keyPrefix">Prepended to every idempotency key before it reaches Redis, so this
    /// store can safely share a Redis instance/database with other data.</param>
    public RedisIdempotencyStore(IConnectionMultiplexer connectionMultiplexer, TimeProvider? timeProvider = null, string keyPrefix = "idempo:")
    {
        _connectionMultiplexer = connectionMultiplexer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _keyPrefix = keyPrefix;
    }

    private IDatabase Database => _connectionMultiplexer.GetDatabase();

    public async Task<IdempotencyReservationResult> TryReserveAsync(
        string key,
        string fingerprintHash,
        TimeSpan pendingTimeout,
        TimeSpan completedTtl,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var pendingTimeoutMs = Math.Max(1, (long)pendingTimeout.TotalMilliseconds);

        var freshEnvelope = new RedisEnvelope
        {
            FingerprintHash = fingerprintHash,
            Status = nameof(IdempotencyRecordStatus.Pending),
            ExpiresAtUnixMs = now.ToUnixTimeMilliseconds() + pendingTimeoutMs,
            RecordJson = JsonSerializer.Serialize(new IdempotencyRecord
            {
                Key = key,
                FingerprintHash = fingerprintHash,
                Status = IdempotencyRecordStatus.Pending,
                CreatedAt = now,
                ExpiresAt = now + pendingTimeout
            }, IdempoJsonContext.Default.IdempotencyRecord)
        };

        var result = (RedisResult[])(await Database.ScriptEvaluateAsync(
            LuaScripts.TryReserve,
            new RedisKey[] { RedisKeyFor(key) },
            new RedisValue[]
            {
                fingerprintHash,
                now.ToUnixTimeMilliseconds(),
                JsonSerializer.Serialize(freshEnvelope, RedisJsonContext.Default.RedisEnvelope),
                pendingTimeoutMs
            }))!;

        var kind = (string)result[0]!;
        return kind switch
        {
            "Reserved" => IdempotencyReservationResult.Reserved,
            "InProgress" => IdempotencyReservationResult.InProgress(ToRecord((string)result[1]!)),
            "Completed" => IdempotencyReservationResult.Completed(ToRecord((string)result[1]!)),
            "Conflict" => IdempotencyReservationResult.Conflict(ToRecord((string)result[1]!)),
            _ => throw new InvalidOperationException($"Unexpected Idempo Redis script result: '{kind}'.")
        };
    }

    public async Task CompleteAsync(string key, IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        var ttlMs = Math.Max(1, (long)(record.ExpiresAt - _timeProvider.GetUtcNow()).TotalMilliseconds);

        var envelope = new RedisEnvelope
        {
            FingerprintHash = record.FingerprintHash,
            Status = nameof(IdempotencyRecordStatus.Completed),
            ExpiresAtUnixMs = record.ExpiresAt.ToUnixTimeMilliseconds(),
            RecordJson = JsonSerializer.Serialize(record, IdempoJsonContext.Default.IdempotencyRecord)
        };

        await Database.ScriptEvaluateAsync(
            LuaScripts.Complete,
            new RedisKey[] { RedisKeyFor(key) },
            new RedisValue[] { JsonSerializer.Serialize(envelope, RedisJsonContext.Default.RedisEnvelope), ttlMs });
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        await Database.ScriptEvaluateAsync(LuaScripts.Release, new RedisKey[] { RedisKeyFor(key) });
    }

    /// <summary>
    /// A no-op: every key this store writes carries a Redis <c>PX</c> expiry matching its own
    /// <c>ExpiresAtUnixMs</c>, so Redis itself already evicts expired entries — there is nothing
    /// left to purge.
    /// </summary>
    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    private RedisKey RedisKeyFor(string key) => _keyPrefix + key;

    private static IdempotencyRecord ToRecord(string envelopeJson)
    {
        var envelope = JsonSerializer.Deserialize(envelopeJson, RedisJsonContext.Default.RedisEnvelope)!;
        return JsonSerializer.Deserialize(envelope.RecordJson, IdempoJsonContext.Default.IdempotencyRecord)!;
    }
}
