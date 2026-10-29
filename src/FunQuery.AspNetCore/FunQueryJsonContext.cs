using System.Text.Json.Serialization;

namespace FunQuery.AspNetCore;

/// <summary>
/// Konteks serialisasi sumber-generated untuk DTO adapter. Tidak ada serializer berbasis
/// reflection, sehingga jalur ini tetap aman bila host di-publish dengan Native AOT.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(QueryErrorResponse))]
internal sealed partial class FunQueryJsonContext : JsonSerializerContext
{
}
