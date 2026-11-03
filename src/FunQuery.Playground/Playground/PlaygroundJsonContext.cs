using System.Text.Json.Serialization;

namespace FunQuery.Playground;

/// <summary>Serialisasi source-generated untuk config playground (aman untuk Native AOT).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlaygroundConfig))]
internal sealed partial class PlaygroundJsonContext : JsonSerializerContext
{
}
