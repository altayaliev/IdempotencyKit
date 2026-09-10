using Microsoft.AspNetCore.Builder;

namespace IdempotencyKit.AspNetCore;

public static class IdempotencyEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Overrides select <see cref="IdempotencyOptions"/> values for this minimal API endpoint.
    /// Any parameter left null falls back to the app-wide <see cref="IdempotencyOptions"/>.
    /// </summary>
    public static TBuilder WithIdempotencyOptions<TBuilder>(
        this TBuilder builder,
        TimeSpan? pendingTimeout = null,
        TimeSpan? completedTtl = null,
        bool? requireHeaderOnMutatingRequests = null,
        long? maxRequestBodyBytes = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new IdempotencyOptionsAttribute
        {
            PendingTimeoutSeconds = pendingTimeout is { } pt ? (int)pt.TotalSeconds : null,
            CompletedTtlSeconds = completedTtl is { } ct ? (int)ct.TotalSeconds : null,
            RequireHeaderOnMutatingRequests = requireHeaderOnMutatingRequests,
            MaxRequestBodyBytes = maxRequestBodyBytes
        });

        return builder;
    }
}
