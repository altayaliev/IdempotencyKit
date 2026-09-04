using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Idempo.AspNetCore;

public static class IdempoServiceCollectionExtensions
{
    /// <summary>
    /// Registers Idempo's default services: an in-memory <see cref="IIdempotencyStore"/> and the
    /// SHA-256 <see cref="IIdempotencyFingerprintProvider"/>. Call <c>app.UseIdempo()</c> to wire in
    /// the middleware. Register your own <see cref="IIdempotencyStore"/> before calling this method
    /// (or after, via <c>Replace</c>) to use a shared backend across instances.
    /// </summary>
    public static IServiceCollection AddIdempo(this IServiceCollection services, Action<IdempotencyOptions>? configure = null)
    {
        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.TryAddSingleton<IIdempotencyFingerprintProvider, Sha256IdempotencyFingerprintProvider>();

        return services;
    }
}
