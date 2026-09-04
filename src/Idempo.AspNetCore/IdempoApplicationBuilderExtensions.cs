using Microsoft.AspNetCore.Builder;

namespace Idempo.AspNetCore;

public static class IdempoApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the idempotency middleware. Must be placed after <c>UseRouting()</c> so endpoint
    /// metadata (e.g. <see cref="IdempotencyIgnoreAttribute"/>) is available, and before the
    /// handler you want protected (e.g. before <c>MapControllers()</c> takes effect).
    /// </summary>
    public static IApplicationBuilder UseIdempo(this IApplicationBuilder app)
    {
        return app.UseMiddleware<IdempotencyMiddleware>();
    }
}
