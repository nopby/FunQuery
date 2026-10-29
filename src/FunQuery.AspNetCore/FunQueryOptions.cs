namespace FunQuery.AspNetCore;

/// <summary>
/// Konfigurasi adapter HTTP. Semua nilai punya default yang aman; ubah lewat
/// <c>AddFunQuery(options =&gt; ...)</c> saat startup, bukan per request.
/// </summary>
public sealed class FunQueryOptions
{
    /// <summary>Path endpoint GET, default <c>/query</c>. Harus dimulai dengan '/'.</summary>
    public string Route { get; set; } = "/query";

    /// <summary>Nama query parameter yang berisi ekspresi FunQuery, default <c>q</c>.</summary>
    public string QueryParameter { get; set; } = "q";

    /// <summary>
    /// Nama query parameter yang memilih source terdaftar, default <c>source</c>. Source yang
    /// dipilih selalu diikat ke variable <c>@source</c>.
    /// </summary>
    public string SourceParameter { get; set; } = "source";

    /// <summary>
    /// Bila true, adapter memetakan dokumen OpenAPI 3.1 di <see cref="OpenApiRoute"/>.
    /// Default false; sample host menyalakannya.
    /// </summary>
    public bool EnableOpenApi { get; set; }

    /// <summary>Path dokumen OpenAPI, default <c>/openapi/v1.json</c>.</summary>
    public string OpenApiRoute { get; set; } = "/openapi/v1.json";

    /// <summary>
    /// Ukuran maksimum body <c>POST /query</c> dalam byte. Default 64 KB. Body yang lebih besar
    /// ditolak sebagai <c>REQUEST_TOO_LARGE</c> (413) tanpa membaca seluruhnya.
    /// </summary>
    public int MaxRequestBodyBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Batas yang dipakai <see cref="QueryEngine"/> untuk setiap request. Default
    /// <see cref="QueryLimits.Default"/> (input 2048 karakter, 1024 token, kedalaman 64).
    /// </summary>
    public QueryLimits Limits { get; set; } = QueryLimits.Default;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Route) || !Route.StartsWith('/'))
            throw new ArgumentException(
                $"{nameof(Route)} must be a non-empty path starting with '/'.",
                nameof(Route));

        if (string.IsNullOrWhiteSpace(QueryParameter))
            throw new ArgumentException(
                $"{nameof(QueryParameter)} must not be empty.",
                nameof(QueryParameter));

        if (string.IsNullOrWhiteSpace(SourceParameter))
            throw new ArgumentException(
                $"{nameof(SourceParameter)} must not be empty.",
                nameof(SourceParameter));

        if (string.IsNullOrWhiteSpace(OpenApiRoute) ||
            !OpenApiRoute.StartsWith('/'))
        {
            throw new ArgumentException(
                $"{nameof(OpenApiRoute)} must be a non-empty path starting with '/'.",
                nameof(OpenApiRoute));
        }

        if (string.Equals(OpenApiRoute, Route, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{nameof(OpenApiRoute)} must differ from {nameof(Route)}.",
                nameof(OpenApiRoute));
        }

        if (MaxRequestBodyBytes < 1)
            throw new ArgumentOutOfRangeException(
                nameof(MaxRequestBodyBytes),
                MaxRequestBodyBytes,
                $"{nameof(MaxRequestBodyBytes)} must be at least 1.");

        ArgumentNullException.ThrowIfNull(Limits, nameof(Limits));
    }
}
