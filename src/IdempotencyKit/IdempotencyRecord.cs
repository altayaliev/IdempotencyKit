namespace IdempotencyKit;

/// <summary>
/// A stored idempotency entry: either a reservation for an in-flight operation
/// (<see cref="IdempotencyRecordStatus.Pending"/>) or a captured result ready for replay
/// (<see cref="IdempotencyRecordStatus.Completed"/>).
/// </summary>
public sealed class IdempotencyRecord
{
    public required string Key { get; init; }

    public required string FingerprintHash { get; init; }

    public IdempotencyRecordStatus Status { get; set; } = IdempotencyRecordStatus.Pending;

    public int? StatusCode { get; set; }

    public byte[]? ResponseBody { get; set; }

    public string? ContentType { get; set; }

    public IReadOnlyDictionary<string, string[]>? Headers { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When this record stops being honored: the pending timeout while <see cref="Status"/> is
    /// <see cref="IdempotencyRecordStatus.Pending"/>, or the completed TTL once it is
    /// <see cref="IdempotencyRecordStatus.Completed"/>.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; set; }
}
