using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace IdempotencyKit.StackExchangeRedis;

public static class IdempotencyKitStackExchangeRedisServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the default in-memory <see cref="IIdempotencyStore"/> with one backed by Redis.
    /// Requires an <see cref="IConnectionMultiplexer"/> to already be registered. Can be called
    /// either before or after <c>AddIdempotencyKit()</c>.
    /// </summary>
    public static IServiceCollection AddIdempotencyKitStackExchangeRedisStore(this IServiceCollection services, string keyPrefix = "idempotencykit:")
    {
        services.Replace(ServiceDescriptor.Singleton<IIdempotencyStore>(sp =>
            new RedisIdempotencyStore(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetService<TimeProvider>(),
                keyPrefix)));

        return services;
    }
}
