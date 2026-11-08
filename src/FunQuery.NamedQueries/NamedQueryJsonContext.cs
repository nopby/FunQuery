using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FunQuery.NamedQueries;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(NamedQueryWriteRequest))]
[JsonSerializable(typeof(NamedQueryResponse))]
[JsonSerializable(typeof(NamedQueryListResponse))]
[JsonSerializable(typeof(NamedQueryAdminError))]
internal sealed partial class NamedQueryJsonContext : JsonSerializerContext
{
    internal static readonly NamedQueryJsonContext Relaxed = new(
        new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        });
}
