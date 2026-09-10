namespace IdempotencyKit.AspNetCore;

/// <summary>
/// Overrides select <see cref="IdempotencyOptions"/> values for one endpoint (or, applied to a
/// controller, all of its actions). Any property left unset falls back to the app-wide
/// <see cref="IdempotencyOptions"/>. Works as a controller/action attribute, and on minimal API
/// endpoints via <see cref="IdempotencyEndpointConventionBuilderExtensions.WithIdempotencyOptions"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IdempotencyOptionsAttribute : Attribute
{
    /// <summary>Overrides <see cref="IdempotencyOptions.PendingTimeout"/>, in seconds.</summary>
    public int? PendingTimeoutSeconds { get; set; }

    /// <summary>Overrides <see cref="IdempotencyOptions.CompletedTtl"/>, in seconds.</summary>
    public int? CompletedTtlSeconds { get; set; }

    /// <summary>Overrides <see cref="IdempotencyOptions.RequireHeaderOnMutatingRequests"/>.</summary>
    public bool? RequireHeaderOnMutatingRequests { get; set; }

    /// <summary>Overrides <see cref="IdempotencyOptions.MaxRequestBodyBytes"/>.</summary>
    public long? MaxRequestBodyBytes { get; set; }
}
