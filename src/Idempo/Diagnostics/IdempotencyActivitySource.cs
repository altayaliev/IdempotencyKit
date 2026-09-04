using System.Diagnostics;

namespace Idempo.Diagnostics;

/// <summary>
/// Idempo's <see cref="System.Diagnostics.ActivitySource"/>, named <see cref="Name"/>. Any
/// OpenTelemetry tracing setup can observe it via <c>.AddSource("Idempo")</c> — no OpenTelemetry
/// package dependency is added by Idempo itself.
/// </summary>
public static class IdempotencyActivitySource
{
    public const string Name = "Idempo";

    public static readonly ActivitySource Instance = new(Name);
}
