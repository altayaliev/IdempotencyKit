namespace IdempotencyKit;

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

    /// <summary>
    /// The longest key value accepted; a longer one is rejected with 400 before it ever reaches
    /// the store. Without this, an attacker (or a buggy client) could send unbounded numbers of
    /// huge, always-unique key values and inflate the store until the next cleanup pass. Matches
    /// the column length <c>IdempotencyKit.EntityFrameworkCore</c>'s default model uses for the key, so a
    /// too-long key is rejected here with a clean 400 rather than surfacing as a raw database error.
    /// </summary>
    public int MaxKeyLength { get; set; } = 200;

    /// <summary>
    /// The largest request body the middleware will buffer to compute a fingerprint and to
    /// support replay. A request whose <c>Content-Length</c> exceeds this passes through
    /// <b>without</b> idempotency protection rather than buffering an unbounded body into memory
    /// — protecting against a large-upload endpoint (accidentally or maliciously) exhausting
    /// memory. Requests without a <c>Content-Length</c> (chunked transfer) are not pre-checked
    /// against this limit and are buffered as read; keep large-upload endpoints off the
    /// <see cref="Methods"/> list, or opt them out with <c>[IdempotencyIgnore]</c>, instead of
    /// relying on this alone.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 1024 * 1024; // 1 MiB
}
