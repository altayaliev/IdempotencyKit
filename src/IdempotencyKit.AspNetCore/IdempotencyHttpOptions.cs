using Microsoft.AspNetCore.Http;

namespace IdempotencyKit.AspNetCore;

/// <summary>
/// ASP.NET Core-specific idempotency options that need an <see cref="HttpContext"/> — kept
/// separate from <see cref="IdempotencyOptions"/> (which has no ASP.NET Core dependency).
/// </summary>
public sealed class IdempotencyHttpOptions
{
    /// <summary>
    /// Transforms the raw <c>Idempotency-Key</c> header value into the key actually used by the
    /// store. Defaults to using the header value as-is. Override this to scope keys — e.g. a
    /// multi-tenant app should prefix the key with a tenant id, so the same client-supplied key
    /// value from two different tenants is never treated as the same operation:
    /// <code>options.KeyResolver = (context, rawKey) =&gt; $"{context.User.FindFirst("tenant_id")?.Value}:{rawKey}";</code>
    /// </summary>
    public Func<HttpContext, string, string> KeyResolver { get; set; } = static (_, rawKey) => rawKey;
}
