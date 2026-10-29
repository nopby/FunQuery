namespace FunQuery.AspNetCore;

/// <summary>
/// Bentuk error HTTP. Kode memakai string stabil dari
/// <see cref="FunQuery.Extensions.QueryErrorCodeExtensions.ToCode"/>; posisi hanya dikirim bila
/// diketahui (lihat <see cref="QueryException.HasPosition"/>).
/// </summary>
internal sealed class QueryErrorResponse
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Position { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Length { get; init; }
}
