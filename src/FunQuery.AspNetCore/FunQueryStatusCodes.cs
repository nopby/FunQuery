using FunQuery.Enums;
using Microsoft.AspNetCore.Http;

namespace FunQuery.AspNetCore;

/// <summary>
/// Pemetaan tunggal dari kode error bahasa ke status HTTP. Semua kode non-internal yang tidak
/// punya arti HTTP khusus adalah kesalahan input, jadi default-nya 400.
/// </summary>
public static class FunQueryStatusCodes
{
    public static int For(QueryErrorCode code) =>
        code switch
        {
            // Ukuran request/ekspresi melebihi batas.
            QueryErrorCode.InputTooLong or
            QueryErrorCode.RequestBodyTooLarge => StatusCodes.Status413PayloadTooLarge,

            // Batas waktu eksekusi.
            QueryErrorCode.ExecutionTimeout => StatusCodes.Status408RequestTimeout,

            // Query valid, tetapi hasil/sumbernya terlalu besar untuk dikirim.
            QueryErrorCode.ResultTooLarge or
            QueryErrorCode.SourceTooLarge => StatusCodes.Status422UnprocessableEntity,

            // Content-Type yang tidak didukung.
            QueryErrorCode.UnsupportedMediaType => StatusCodes.Status415UnsupportedMediaType,

            // Named query tidak ditemukan / dinonaktifkan.
            QueryErrorCode.UnknownNamedQuery or
            QueryErrorCode.NamedQueryDisabled => StatusCodes.Status404NotFound,

            QueryErrorCode.NamedQueryForbidden => StatusCodes.Status403Forbidden,

            // Otorisasi.
            QueryErrorCode.Unauthenticated => StatusCodes.Status401Unauthorized,
            QueryErrorCode.Forbidden => StatusCodes.Status403Forbidden,

            // Catalog tidak tersedia atau tidak dapat di-resolve.
            QueryErrorCode.NamedQueriesUnavailable => StatusCodes.Status503ServiceUnavailable,

            // Catalog berisi query yang tidak valid; ini kesalahan server.
            QueryErrorCode.InvalidNamedQuery => StatusCodes.Status500InternalServerError,

            // Bug di server, bukan kesalahan pemanggil.
            QueryErrorCode.InternalError => StatusCodes.Status500InternalServerError,

            // Semua error lexer, parser, analyzer, dan runtime karena input.
            _ => StatusCodes.Status400BadRequest,
        };
}
