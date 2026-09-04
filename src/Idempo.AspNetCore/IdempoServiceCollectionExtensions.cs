using Idempo.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Idempo.AspNetCore;

public static class IdempoServiceCollectionExtensions
{
    /// <summary>
    /// Registers Idempo's default services: an in-memory <see cref="IIdempotencyStore"/>, the
    /// SHA-256 <see cref="IIdempotencyFingerprintProvider"/>, and a background
    /// <see cref="IdempotencyCleanupService"/> that periodically purges expired entries. Call
    /// <c>app.UseIdempo()</c> to wire in the middleware. Register your own
    /// <see cref="IIdempotencyStore"/> before calling this method (or after, via <c>Replace</c>)
    /// to use a shared backend across instances.
    /// </summary>
    public static IServiceCollection AddIdempo(
        this IServiceCollection services,
        Action<IdempotencyOptions>? configure = null,
        Action<IdempotencyCleanupOptions>? configureCleanup = null,
        Action<IdempotencyHttpOptions>? configureHttp = null)
    {
        services.AddOptions();

        var optionsBuilder = services.AddOptions<IdempotencyOptions>().ValidateOnStart();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        if (configureCleanup is not null)
        {
            services.Configure(configureCleanup);
        }

        if (configureHttp is not null)
        {
            services.Configure(configureHttp);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsValidator>());

        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.TryAddSingleton<IIdempotencyFingerprintProvider, Sha256IdempotencyFingerprintProvider>();
        services.TryAddSingleton<IdempotencyMetrics>();
        services.AddHostedService<IdempotencyCleanupService>();

        return services;
    }
}
