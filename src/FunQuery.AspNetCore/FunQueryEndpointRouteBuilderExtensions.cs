using System.Text;
using FunQuery.Enums;
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

        endpoints
            .MapGet(
                options.Route,
                (HttpContext context, QueryEngine engine) => HandleGet(context, engine, options))
            .AddEndpointFilter(QueryErrorFilter);

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
            throw new QueryException(
                QueryErrorCode.InvalidRequest,
                $"The '{options.QueryParameter}' query parameter is required.");
        }

        return Results.Text(
            engine.Execute(query).ToJson(),
            "application/json",
            Encoding.UTF8);
    }

    /// <summary>
    /// Satu pintu untuk semua kegagalan endpoint: input error memakai kode dan posisinya,
    /// exception tak terduga menjadi 500 generik tanpa membocorkan detail.
    /// </summary>
    private static async ValueTask<object?> QueryErrorFilter(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            // Klien sudah pergi; tidak ada respons yang perlu ditulis.
            throw;
        }
        catch (QueryException error)
        {
            return QueryErrorResults.ToResult(QueryErrorResults.From(error));
        }
        catch (Exception error)
        {
            context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("FunQuery.AspNetCore")
                .LogError(error, "The query failed with an unexpected error.");

            return QueryErrorResults.ToResult(QueryErrorResults.Unexpected());
        }
    }
}
