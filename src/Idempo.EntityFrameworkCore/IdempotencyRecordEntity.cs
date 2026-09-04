namespace Idempo.EntityFrameworkCore;

/// <summary>
/// The database shape of an <see cref="IdempotencyRecord"/>. Kept separate from the public
/// core model so EF Core owns a plain, mutable, provider-agnostic entity to materialize —
/// the header dictionary is stored pre-serialized as JSON rather than requiring a value
/// converter with dictionary change-tracking semantics.
/// </summary>
public sealed class IdempotencyRecordEntity
{
    public string Key { get; set; } = string.Empty;

    public string FingerprintHash { get; set; } = string.Empty;

    public IdempotencyRecordStatus Status { get; set; }

    public int? StatusCode { get; set; }

    public byte[]? ResponseBody { get; set; }

    public string? ContentType { get; set; }

    public string? HeadersJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Unix milliseconds rather than <see cref="DateTimeOffset"/>: the SQLite provider cannot
    /// translate a <c>&lt;</c>/<c>&lt;=</c>/<c>&gt;</c>/<c>&gt;=</c> comparison on a
    /// <see cref="DateTimeOffset"/> column into SQL at all (only equality), which both the
    /// expired-reclaim and <see cref="EfCoreIdempotencyStore{TContext}.PurgeExpiredAsync"/>
    /// queries need. A <see cref="long"/> compares the same way in every relational provider.
    /// </summary>
    public long ExpiresAtUnixMs { get; set; }
}
