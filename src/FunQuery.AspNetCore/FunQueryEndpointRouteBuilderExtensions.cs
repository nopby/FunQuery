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

        // RequestDelegate (bukan Delegate) agar endpoint tetap kompatibel dengan Native AOT:
        // tidak ada reflection untuk mengikat parameter handler.
        RequestDelegate get = context => HandleGetAsync(context, options);
        RequestDelegate post = context => HandlePostAsync(context, options);

        endpoints.MapGet(options.Route, get);
        endpoints.MapPost(options.Route, post);

        return endpoints;
    }

    private static Task HandleGetAsync(HttpContext context, FunQueryOptions options) =>
        RunWithErrorsAsync(context, async () =>
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

            return await ExecuteAsync(context, options, query, sourceName, variables);
        });

    private static Task HandlePostAsync(HttpContext context, FunQueryOptions options) =>
        RunWithErrorsAsync(context, async () =>
        {
            var request = await QueryRequestReader.ReadAsync(context, options);
            var query = request.Query;

            if (string.IsNullOrWhiteSpace(query))
                throw InvalidRequest("The 'query' field is required.");

            var variables = QueryRequestReader.ReadVariables(request, options.Limits);

            return await ExecuteAsync(
                context,
                options,
                query,
                request.Source,
                variables);
        });

    private static async Task<IResult> ExecuteAsync(
        HttpContext context,
        FunQueryOptions options,
        string query,
        string? sourceName,
        QueryEnvironment variables)
    {
        var engine = context.RequestServices.GetRequiredService<QueryEngine>();
        var sources = context.RequestServices.GetRequiredService<SourceRegistry>();

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

        if (source is not null && source.Executor is not null)
        {
            var result = await source.Executor.ExecuteAsync(
                plan,
                variables,
                context.RequestAborted);

            return Results.Text(
                ValueJson.Serialize(result),
                "application/json",
                Encoding.UTF8);
        }

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
            engine.ExecutePlan(plan, valueEnvironment, context.RequestAborted).ToJson(),
            "application/json",
            Encoding.UTF8);
    }

    /// <summary>
    /// Satu pintu untuk semua kegagalan endpoint: input error memakai kode dan posisinya,
    /// exception tak terduga menjadi 500 generik tanpa membocorkan detail.
    /// </summary>
    private static async Task RunWithErrorsAsync(
        HttpContext context,
        Func<Task<IResult>> action)
    {
        try
        {
            var result = await action();
            await result.ExecuteAsync(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Klien sudah pergi; tidak ada respons yang perlu ditulis.
            throw;
        }
        catch (QueryException error)
        {
            await QueryErrorResults
                .ToResult(QueryErrorResults.From(error))
                .ExecuteAsync(context);
        }
        catch (Exception error)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("FunQuery.AspNetCore")
                .LogError(error, "The query failed with an unexpected error.");

            await QueryErrorResults
                .ToResult(QueryErrorResults.Unexpected())
                .ExecuteAsync(context);
        }
    }

    private static QueryException InvalidRequest(string message) =>
        new(QueryErrorCode.InvalidRequest, message);
}
