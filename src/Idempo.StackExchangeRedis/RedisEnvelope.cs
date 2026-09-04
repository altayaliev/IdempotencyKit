namespace Idempo.StackExchangeRedis;

/// <summary>
/// What is actually stored in Redis for one key. <see cref="FingerprintHash"/>,
/// <see cref="Status"/>, and <see cref="ExpiresAtUnixMs"/> are duplicated as flat fields purely
/// so the Lua scripts can inspect them with plain field access — they never need to understand
/// the shape of <see cref="RecordJson"/>, which is the opaque, canonical <see cref="IdempotencyRecord"/>
/// that C# deserializes on the way out.
/// </summary>
internal sealed class RedisEnvelope
{
    public string FingerprintHash { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public long ExpiresAtUnixMs { get; set; }

    public string RecordJson { get; set; } = string.Empty;
}
