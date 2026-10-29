using System.Text;
using System.Text.Json;
using FunQuery.Enums;
using FunQuery.Extensions;
using Microsoft.AspNetCore.Http;

namespace FunQuery.AspNetCore;

/// <summary>
/// Membangun envelope error yang sama untuk setiap kegagalan. Detail internal tidak pernah
/// dikirim ke klien: hanya kode <c>INTERNAL_ERROR</c> dan pesan generik.
/// </summary>
internal static class QueryErrorResults
{
    internal const string InternalMessage = "The query could not be executed.";

    internal static (QueryErrorResponse Response, int StatusCode) From(QueryException error)
    {
        if (error.IsInternal)
            return Unexpected();

        return (
            new QueryErrorResponse
            {
                Code = error.CodeName,
                Message = error.Message,
                Position = error.HasPosition ? error.Position : null,
                Length = error.HasPosition ? error.Length : null,
            },
            FunQueryStatusCodes.For(error.Code));
    }

    internal static (QueryErrorResponse Response, int StatusCode) Unexpected() =>
        (
            new QueryErrorResponse
            {
                Code = QueryErrorCode.InternalError.ToCode(),
                Message = InternalMessage,
            },
            FunQueryStatusCodes.For(QueryErrorCode.InternalError));

    internal static IResult ToResult((QueryErrorResponse Response, int StatusCode) error) =>
        Results.Text(
            JsonSerializer.Serialize(error.Response, FunQueryJsonContext.Relaxed.QueryErrorResponse),
            "application/json",
            Encoding.UTF8,
            error.StatusCode);
}
