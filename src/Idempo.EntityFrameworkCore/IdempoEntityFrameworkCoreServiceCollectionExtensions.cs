using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Idempo.EntityFrameworkCore;

public static class IdempoEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the default in-memory <see cref="IIdempotencyStore"/> with one backed by
    /// <typeparamref name="TContext"/>. Requires <c>services.AddDbContextFactory&lt;TContext&gt;(...)</c>
    /// to already be registered, and <typeparamref name="TContext"/>'s model to call
    /// <see cref="IdempotencyModelBuilderExtensions.ConfigureIdempotencyStore"/> from
    /// <c>OnModelCreating</c>. Can be called either before or after <c>AddIdempo()</c>.
    /// </summary>
    public static IServiceCollection AddIdempoEntityFrameworkCoreStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.Replace(ServiceDescriptor.Singleton<IIdempotencyStore, EfCoreIdempotencyStore<TContext>>());
        return services;
    }
}
