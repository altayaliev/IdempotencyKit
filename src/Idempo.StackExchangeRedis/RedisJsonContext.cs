using System.Text.Json.Serialization;

namespace Idempo.StackExchangeRedis;

[JsonSerializable(typeof(RedisEnvelope))]
internal sealed partial class RedisJsonContext : JsonSerializerContext
{
}
