namespace IdempotencyKit.AspNetCore;

/// <summary>Opts an endpoint out of the global idempotency middleware.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IdempotencyIgnoreAttribute : Attribute
{
}
