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

            // Bug di server, bukan kesalahan pemanggil.
            QueryErrorCode.InternalError => StatusCodes.Status500InternalServerError,

            // Semua error lexer, parser, analyzer, dan runtime karena input.
            _ => StatusCodes.Status400BadRequest,
        };
}
