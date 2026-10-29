using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FunQuery.AspNetCore;

/// <summary>
/// Konteks serialisasi sumber-generated untuk DTO adapter. Tidak ada serializer berbasis
/// reflection, sehingga jalur ini tetap aman bila host di-publish dengan Native AOT.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(QueryErrorResponse))]
[JsonSerializable(typeof(QueryRequest))]
internal sealed partial class FunQueryJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Sama dengan konteks default, tetapi tidak meng-escape karakter seperti tanda kutip
    /// tunggal. Encoding ini tetap JSON yang valid; hasilnya konsisten dengan ValueJson.
    /// </summary>
    internal static readonly FunQueryJsonContext Relaxed = new(
        new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
}
