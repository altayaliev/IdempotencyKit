using System.Text.Json.Serialization;

namespace IdempotencyKit.StackExchangeRedis;

[JsonSerializable(typeof(RedisEnvelope))]
internal sealed partial class RedisJsonContext : JsonSerializerContext
{
}
