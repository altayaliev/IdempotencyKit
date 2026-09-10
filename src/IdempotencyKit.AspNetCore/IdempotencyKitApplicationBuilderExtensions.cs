using Microsoft.AspNetCore.Builder;

namespace IdempotencyKit.AspNetCore;

public static class IdempotencyKitApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the idempotency middleware. Must be placed after <c>UseRouting()</c> so endpoint
    /// metadata (e.g. <see cref="IdempotencyIgnoreAttribute"/>) is available, and before the
    /// handler you want protected (e.g. before <c>MapControllers()</c> takes effect).
    /// </summary>
    public static IApplicationBuilder UseIdempotencyKit(this IApplicationBuilder app)
    {
        return app.UseMiddleware<IdempotencyMiddleware>();
    }
}
