namespace Idempo.StackExchangeRedis;

/// <summary>
/// Every atomicity guarantee for the Redis backend lives in these scripts — Redis executes a
/// Lua script as a single atomic step with no other command interleaved, which is what makes
/// the check-then-act "reserve" logic race-safe without a separate distributed-lock library.
/// </summary>
internal static class LuaScripts
{
    /// <summary>
    /// KEYS[1] = redis key. ARGV[1] = fingerprintHash, ARGV[2] = now (unix ms),
    /// ARGV[3] = fresh Pending envelope JSON to write if reserving, ARGV[4] = pending timeout (ms).
    /// Returns {"Reserved"} or {"InProgress"|"Completed"|"Conflict", existingEnvelopeJson}.
    /// </summary>
    public const string TryReserve = """
        local raw = redis.call('GET', KEYS[1])

        if not raw then
            redis.call('SET', KEYS[1], ARGV[3], 'PX', ARGV[4])
            return {'Reserved'}
        end

        local existing = cjson.decode(raw)
        local now = tonumber(ARGV[2])

        if existing.ExpiresAtUnixMs <= now then
            redis.call('SET', KEYS[1], ARGV[3], 'PX', ARGV[4])
            return {'Reserved'}
        end

        if existing.FingerprintHash ~= ARGV[1] then
            return {'Conflict', raw}
        end

        if existing.Status == 'Completed' then
            return {'Completed', raw}
        end

        return {'InProgress', raw}
        """;

    /// <summary>
    /// KEYS[1] = redis key. ARGV[1] = completed envelope JSON, ARGV[2] = completed TTL (ms).
    /// Only overwrites a reservation this caller still Pending-ly owns.
    /// </summary>
    public const string Complete = """
        local raw = redis.call('GET', KEYS[1])

        if raw then
            local existing = cjson.decode(raw)
            if existing.Status == 'Pending' then
                redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
            end
        end

        return nil
        """;

    /// <summary>
    /// KEYS[1] = redis key. Only removes a reservation that is still Pending, so releasing after
    /// a completed result was already stored (or already reclaimed by someone else) is a no-op.
    /// </summary>
    public const string Release = """
        local raw = redis.call('GET', KEYS[1])

        if raw then
            local existing = cjson.decode(raw)
            if existing.Status == 'Pending' then
                redis.call('DEL', KEYS[1])
            end
        end

        return nil
        """;
}
