using System.Text;
using FunQuery.Caching;
using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;

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

        if (options.EnableOpenApi)
        {
            RequestDelegate openApi = context => WriteOpenApiAsync(context, options);
            endpoints.MapGet(options.OpenApiRoute, openApi);
        }

        if (options.EnableApiDocs)
        {
            endpoints.MapScalarApiReference(options.ApiDocsRoute, (scalar, httpContext) =>
            {
                // Scalar memperlakukan route pattern sebagai URL relatif terhadap /docs/.
                // Pakai URL absolut agar dokumen tetap ditemukan saat ApiDocsRoute diubah.
                var documentUrl =
                    $"{httpContext.Request.Scheme}://{httpContext.Request.Host}" +
                    $"{httpContext.Request.PathBase}{options.OpenApiRoute}";

                scalar
                    .WithTitle("FunQuery API")
                    .WithOpenApiRoutePattern(documentUrl)
                    .DisableDefaultFonts()
                    .EnableDarkMode()
                    .WithNonce();
            });
        }

        return endpoints;
    }

    private static async Task WriteOpenApiAsync(
        HttpContext context,
        FunQueryOptions options)
    {
        var json = FunQueryOpenApiDocument.Create(
            options.Route,
            options.QueryParameter,
            options.SourceParameter);

        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store";
        await context.Response.WriteAsync(json, context.RequestAborted);
    }

    private static Task HandleGetAsync(HttpContext context, FunQueryOptions options) =>
        RunWithErrorsAsync(context, async () =>
        {
            var queryValues = context.Request.Query[options.QueryParameter];
            var query = queryValues.Count > 0 ? queryValues[0] : null;
            var key = context.Request.Query[options.KeyParameter].FirstOrDefault();
            var sourceName = context.Request.Query[options.SourceParameter].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(key))
            {
                if (!string.IsNullOrWhiteSpace(query))
                {
                    throw InvalidRequest(
                        $"Use either '{options.QueryParameter}' or '{options.KeyParameter}', not both.");
                }

                if (!string.IsNullOrWhiteSpace(sourceName))
                {
                    throw InvalidRequest(
                        "The 'source' parameter cannot be used with a named query.");
                }
            }
            else if (string.IsNullOrWhiteSpace(query))
            {
                throw InvalidRequest(
                    $"The '{options.QueryParameter}' or '{options.KeyParameter}' " +
                    "query parameter is required.");
            }

            var variables = QueryStringVariables.Read(context.Request.Query, options.Limits);

            return await ExecuteAsync(
                context,
                options,
                query,
                sourceName,
                key,
                variables);
        });

    private static Task HandlePostAsync(HttpContext context, FunQueryOptions options) =>
        RunWithErrorsAsync(context, async () =>
        {
            var request = await QueryRequestReader.ReadAsync(context, options);
            var query = request.Query;
            var key = request.Key;

            if (!string.IsNullOrWhiteSpace(key))
            {
                if (!string.IsNullOrWhiteSpace(query))
                {
                    throw InvalidRequest(
                        "Use either 'query' or 'key', not both.");
                }

                if (!string.IsNullOrWhiteSpace(request.Source))
                {
                    throw InvalidRequest(
                        "The 'source' field cannot be used with a named query.");
                }
            }
            else if (string.IsNullOrWhiteSpace(query))
            {
                throw InvalidRequest("The 'query' or 'key' field is required.");
            }

            var variables = QueryRequestReader.ReadVariables(request, options.Limits);

            return await ExecuteAsync(
                context,
                options,
                query,
                request.Source,
                key,
                variables);
        });

    private static async Task<IResult> ExecuteAsync(
        HttpContext context,
        FunQueryOptions options,
        string? query,
        string? sourceName,
        string? key,
        QueryEnvironment variables)
    {
        var engine = context.RequestServices.GetRequiredService<QueryEngine>();
        var sources = context.RequestServices.GetRequiredService<SourceRegistry>();

        NamedQueryResolution? named = null;

        if (!string.IsNullOrWhiteSpace(key))
        {
            named = await ResolveNamedAsync(
                context,
                key,
                context.RequestAborted);

            query = named.Query;
            sourceName = named.SourceName;
        }

        if (string.IsNullOrWhiteSpace(query))
            throw InvalidRequest("A query or named-query key is required.");

        IQuerySource? source = null;

        if (!string.IsNullOrEmpty(sourceName))
        {
            if (!sources.TryGet(sourceName, out source))
            {
                throw NamedSourceMissing(sourceName, named);
            }
        }

        var typeEnvironment = variables;

        if (source is not null)
        {
            typeEnvironment = typeEnvironment.With(
                QueryVariableValues.SourceVariableName,
                new QueryParameter(new ArrayType(source.Schema), null));
        }

        // Resolve $named before touching the cache or provider. The request may also use
        // ?key=, in which case this prepare validates the resolved query text.
        var prepared = engine.Prepare(query, typeEnvironment);

        if (prepared.NamedQueryKey is not null)
        {
            if (named is not null)
            {
                throw new QueryException(
                    QueryErrorCode.InvalidNamedQuery,
                    "A named query cannot invoke another $named.");
            }

            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                throw InvalidRequest(
                    "The 'source' parameter cannot be used with $named.");
            }

            named = await ResolveNamedAsync(
                context,
                prepared.NamedQueryKey,
                context.RequestAborted);

            query = named.Query;
            sourceName = named.SourceName;
            source = null;

            if (!string.IsNullOrEmpty(sourceName))
            {
                if (!sources.TryGet(sourceName, out source))
                {
                    throw NamedSourceMissing(sourceName, named);
                }
            }
        }

        var cache = context.RequestServices.GetService<IQueryResultCache>();

        if (cache is not null && cache.Enabled)
        {
            var keyBuilder = context.RequestServices
                .GetRequiredService<IQueryCacheKeyBuilder>();
            var scope = context.RequestServices
                .GetService<IQueryCacheScopeProvider>()
                ?.GetScope(context);
            var cacheKey = named is null
                ? keyBuilder.Create(query, sourceName, variables, scope)
                : keyBuilder.CreateNamed(
                    named.AppId,
                    named.TenantId,
                    named.Key,
                    named.Version,
                    query,
                    sourceName,
                    variables);
            var policy = named is null
                ? QueryCachePolicy.Default
                : ToCachePolicy(named);

            var payload = await cache.GetOrCreateAsync(
                cacheKey,
                token => ExecutePayloadAsync(
                    engine,
                    source,
                    options,
                    query,
                    variables,
                    token),
                policy,
                context.RequestAborted);

            return Results.Bytes(
                payload,
                "application/json; charset=utf-8");
        }

        var direct = await ExecutePayloadAsync(
            engine,
            source,
            options,
            query,
            variables,
            context.RequestAborted);

        return Results.Bytes(
            direct,
            "application/json; charset=utf-8");
    }

    private static QueryCachePolicy ToCachePolicy(NamedQueryResolution named) =>
        named.CacheMode switch
        {
            "none" => QueryCachePolicy.Disabled,
            "custom" => new QueryCachePolicy(
                QueryCacheMode.Custom,
                named.L1TtlSeconds is > 0
                    ? TimeSpan.FromSeconds(named.L1TtlSeconds.Value)
                    : null,
                named.L2TtlSeconds is > 0
                    ? TimeSpan.FromSeconds(named.L2TtlSeconds.Value)
                    : null),
            _ => QueryCachePolicy.Default,
        };

    private static QueryException NamedSourceMissing(
        string sourceName,
        NamedQueryResolution? named) =>
        named is not null
            ? new QueryException(
                QueryErrorCode.InvalidNamedQuery,
                $"Named query '{named.Key}' references unregistered source '{sourceName}'.")
            : new QueryException(
                QueryErrorCode.UnknownSource,
                $"Unknown source '{sourceName}'.");

    private static async ValueTask<NamedQueryResolution> ResolveNamedAsync(
        HttpContext context,
        string key,
        CancellationToken cancellationToken)
    {
        var resolver = context.RequestServices.GetService<INamedQueryResolver>()
            ?? throw new QueryException(
                QueryErrorCode.NamedQueriesUnavailable,
                "Named queries are not configured on this host.");

        var scope = context.RequestServices
            .GetRequiredService<INamedQueryScopeProvider>()
            .GetScope(context);

        var access = context.RequestServices
            .GetRequiredService<INamedQueryAccessProvider>();

        if (!await access.CanInvokeAsync(
                context,
                scope,
                key,
                cancellationToken))
        {
            throw new QueryException(
                QueryErrorCode.NamedQueryForbidden,
                $"Named query '{key}' is not available to this caller.");
        }

        return await resolver.ResolveAsync(
            scope.AppId,
            scope.TenantId,
            key,
            cancellationToken);
    }

    private static async ValueTask<byte[]> ExecutePayloadAsync(
        QueryEngine engine,
        IQuerySource? source,
        FunQueryOptions options,
        string query,
        QueryEnvironment variables,
        CancellationToken cancellationToken)
    {
        var typeEnvironment = variables;

        if (source is not null)
        {
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
                cancellationToken);

            return Encoding.UTF8.GetBytes(ValueJson.Serialize(result));
        }

        var valueEnvironment = variables;

        if (source is not null)
        {
            var rows = await source.LoadAsync(cancellationToken);

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

        return Encoding.UTF8.GetBytes(
            engine.ExecutePlan(plan, valueEnvironment, cancellationToken).ToJson());
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
