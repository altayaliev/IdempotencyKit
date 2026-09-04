namespace Idempo;

public sealed class IdempotencyOptions
{
    /// <summary>The request header that carries the client-supplied idempotency key.</summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>HTTP methods the middleware considers for idempotency. Others pass through untouched.</summary>
    public ISet<string> Methods { get; set; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    /// <summary>
    /// How long a reservation is honored while its operation is still running. Should comfortably
    /// exceed the slowest expected request; a reservation older than this is treated as abandoned
    /// (e.g. the process crashed) and is released for retry.
    /// </summary>
    public TimeSpan PendingTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a completed result stays available for replay.</summary>
    public TimeSpan CompletedTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// When true, a mutating request (see <see cref="Methods"/>) without an idempotency key header
    /// is rejected with 400 instead of passing through unprotected.
    /// </summary>
    public bool RequireHeaderOnMutatingRequests { get; set; }
}
