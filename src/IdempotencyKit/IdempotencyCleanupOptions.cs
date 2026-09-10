namespace IdempotencyKit;

public sealed class IdempotencyCleanupOptions
{
    /// <summary>How often <see cref="IdempotencyCleanupService"/> asks the store to purge expired entries.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}
