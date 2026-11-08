using System.Text.Json;

namespace FunQuery.AspNetCore;

/// <summary>
/// Body <c>POST /query</c>. Properti yang tidak dikenal diabaikan agar klien lama tidak rusak
/// saat field baru ditambahkan. Lihat docs/HttpApi.md.
/// </summary>
public sealed class QueryRequest
{
    /// <summary>Named-query key. Tidak boleh dikirim bersamaan dengan <see cref="Query"/>.</summary>
    public string? Key { get; init; }

    /// <summary>Ekspresi FunQuery. Wajib bila <see cref="Key"/> tidak ada.</summary>
    public string? Query { get; init; }

    /// <summary>Nama source terdaftar; bila ada, diikat ke <c>@source</c>.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// Variabel <c>@nama</c> sebagai object JSON. Nilai harus scalar (string, number, bool,
    /// null); array/object ditolak pada v1.
    /// </summary>
    public JsonElement? Variables { get; init; }
}
