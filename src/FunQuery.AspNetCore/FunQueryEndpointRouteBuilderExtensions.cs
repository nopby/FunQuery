using System.Text;
using System.Text.Json;
using FunQuery.Enums;
using FunQuery.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FunQuery.AspNetCore;

public static class FunQueryEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Memetakan <c>GET /query?q=...</c>. Query dijalankan lewat <see cref="QueryEngine"/> yang
    /// didaftarkan <see cref="ServiceCollectionExtensions.AddFunQuery"/>; hasilnya dikirim sebagai
    /// JSON apa adanya (tidak dibungkus envelope).
    /// </summary>
    public static IEndpointRouteBuilder MapFunQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<FunQueryOptions>();

        endpoints.MapGet(
            options.Route,
            (HttpContext context, QueryEngine engine) => HandleGet(context, engine, options));

        return endpoints;
    }

    private static IResult HandleGet(
        HttpContext context,
        QueryEngine engine,
        FunQueryOptions options)
    {
        var values = context.Request.Query[options.QueryParameter];
        var query = values.Count > 0 ? values[0] : null;

        if (string.IsNullOrWhiteSpace(query))
        {
            return Error(
                QueryErrorCode.InvalidRequest,
                $"The '{options.QueryParameter}' query parameter is required.",
                StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = engine.Execute(query);

            return Results.Text(
                result.ToJson(),
                "application/json",
                Encoding.UTF8);
        }
        catch (QueryException error) when (!error.IsInternal)
        {
            return Error(error, StatusCodes.Status400BadRequest);
        }
        catch (QueryException error)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("FunQuery.AspNetCore")
                .LogError(error, "The query failed with an internal error.");

            return Error(
                QueryErrorCode.InternalError,
                "The query could not be executed.",
                StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult Error(QueryException error, int statusCode) =>
        Error(
            error.Code,
            error.Message,
            statusCode,
            error.HasPosition ? error.Position : null,
            error.HasPosition ? error.Length : null);

    private static IResult Error(
        QueryErrorCode code,
        string message,
        int statusCode,
        int? position = null,
        int? length = null)
    {
        var response = new QueryErrorResponse
        {
            Code = code.ToCode(),
            Message = message,
            Position = position,
            Length = length,
        };

        return Results.Text(
            JsonSerializer.Serialize(response, FunQueryJsonContext.Default.QueryErrorResponse),
            "application/json",
            Encoding.UTF8,
            statusCode);
    }
}
