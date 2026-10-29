using System.Text;
using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FunQuery.AspNetCore;

public static class FunQueryEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Memetakan <c>GET /query</c> dan <c>POST /query</c>. Keduanya memakai pipeline yang sama
    /// (analisis, source, eksekusi) dan envelope error yang sama; hanya cara membaca query,
    /// source, dan variabel yang berbeda.
    /// </summary>
    public static IEndpointRouteBuilder MapFunQueryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<FunQueryOptions>();

        endpoints
            .MapGet(
                options.Route,
                (HttpContext context, QueryEngine engine, SourceRegistry sources) =>
                    HandleGetAsync(context, engine, options, sources))
            .AddEndpointFilter(QueryErrorFilter);

        endpoints
            .MapPost(
                options.Route,
                (HttpContext context, QueryEngine engine, SourceRegistry sources) =>
                    HandlePostAsync(context, engine, options, sources))
            .AddEndpointFilter(QueryErrorFilter);

        return endpoints;
    }

    private static async Task<IResult> HandleGetAsync(
        HttpContext context,
        QueryEngine engine,
        FunQueryOptions options,
        SourceRegistry sources)
    {
        var queryValues = context.Request.Query[options.QueryParameter];
        var query = queryValues.Count > 0 ? queryValues[0] : null;

        if (string.IsNullOrWhiteSpace(query))
        {
            throw InvalidRequest(
                $"The '{options.QueryParameter}' query parameter is required.");
        }

        var sourceName = context.Request.Query[options.SourceParameter].FirstOrDefault();
        var variables = QueryStringVariables.Read(context.Request.Query, options.Limits);

        return await ExecuteAsync(
            context,
            engine,
            options,
            sources,
            query,
            sourceName,
            variables);
    }

    private static async Task<IResult> HandlePostAsync(
        HttpContext context,
        QueryEngine engine,
        FunQueryOptions options,
        SourceRegistry sources)
    {
        var request = await QueryRequestReader.ReadAsync(context, options);
        var query = request.Query;

        if (string.IsNullOrWhiteSpace(query))
            throw InvalidRequest("The 'query' field is required.");

        var variables = QueryRequestReader.ReadVariables(request, options.Limits);

        return await ExecuteAsync(
            context,
            engine,
            options,
            sources,
            query,
            request.Source,
            variables);
    }

    private static async Task<IResult> ExecuteAsync(
        HttpContext context,
        QueryEngine engine,
        FunQueryOptions options,
        SourceRegistry sources,
        string query,
        string? sourceName,
        QueryEnvironment variables)
    {
        IQuerySource? source = null;
        var typeEnvironment = variables;

        if (!string.IsNullOrEmpty(sourceName))
        {
            if (!sources.TryGet(sourceName, out source))
            {
                throw new QueryException(
                    QueryErrorCode.UnknownSource,
                    $"Unknown source '{sourceName}'.");
            }

            // Analyzer hanya butuh tipe source, belum datanya.
            typeEnvironment = typeEnvironment.With(
                QueryVariableValues.SourceVariableName,
                new QueryParameter(new ArrayType(source.Schema), null));
        }

        // Validasi query terhadap schema sebelum satu baris pun dibaca. Unknown field atau
        // perbandingan yang salah tipe gagal di sini, bukan setelah source dimuat.
        var plan = engine.Prepare(query, typeEnvironment);

        var valueEnvironment = variables;

        if (source is not null)
        {
            var rows = await source.LoadAsync(context.RequestAborted);

            if (rows.Count > options.Limits.MaxSourceItems)
            {
                throw new QueryException(
                    QueryErrorCode.SourceTooLarge,
                    $"Source '{source.Name}' has more than " +
                    $"{options.Limits.MaxSourceItems} rows.");
            }

            var materialized = SourceRows.Materialize(source.Name, source.Schema, rows);

            valueEnvironment = valueEnvironment.With(
                QueryVariableValues.SourceVariableName,
                new QueryParameter(new ArrayType(source.Schema), materialized));
        }

        return Results.Text(
            engine.ExecutePlan(plan, valueEnvironment).ToJson(),
            "application/json",
            Encoding.UTF8);
    }

    private static QueryException InvalidRequest(string message) =>
        new(QueryErrorCode.InvalidRequest, message);

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
