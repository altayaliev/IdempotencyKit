using System.Text.Json.Serialization;

namespace Idempo;

/// <summary>
/// System.Text.Json source-generated (de)serialization for the types Idempo itself persists —
/// no reflection-based fallback needed, and it stays trim/AOT-compatible. Public so a custom
/// <see cref="IIdempotencyStore"/> implementation can reuse
/// <see cref="IdempotencyRecord"/>/<see cref="IReadOnlyDictionary{TKey,TValue}"/> serialization
/// without its own reflection-based context.
/// </summary>
[JsonSerializable(typeof(IdempotencyRecord))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string[]>))]
public sealed partial class IdempoJsonContext : JsonSerializerContext
{
}
