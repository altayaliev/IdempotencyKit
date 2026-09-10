using System.Diagnostics;

namespace IdempotencyKit.Diagnostics;

/// <summary>
/// IdempotencyKit's <see cref="System.Diagnostics.ActivitySource"/>, named <see cref="Name"/>. Any
/// OpenTelemetry tracing setup can observe it via <c>.AddSource("IdempotencyKit")</c> — no OpenTelemetry
/// package dependency is added by IdempotencyKit itself.
/// </summary>
public static class IdempotencyActivitySource
{
    public const string Name = "IdempotencyKit";

    public static readonly ActivitySource Instance = new(Name);
}
