using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Idempo.StackExchangeRedis;

public static class IdempoStackExchangeRedisServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the default in-memory <see cref="IIdempotencyStore"/> with one backed by Redis.
    /// Requires an <see cref="IConnectionMultiplexer"/> to already be registered. Can be called
    /// either before or after <c>AddIdempo()</c>.
    /// </summary>
    public static IServiceCollection AddIdempoStackExchangeRedisStore(this IServiceCollection services, string keyPrefix = "idempo:")
    {
        services.Replace(ServiceDescriptor.Singleton<IIdempotencyStore>(sp =>
            new RedisIdempotencyStore(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetService<TimeProvider>(),
                keyPrefix)));

        return services;
    }
}
