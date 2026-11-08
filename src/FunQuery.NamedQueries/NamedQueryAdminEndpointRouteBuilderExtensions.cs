using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FunQuery.NamedQueries;

public static class NamedQueryAdminEndpointRouteBuilderExtensions
{
    private const int MaxBodyBytes = 64 * 1024;

    /// <summary>
    /// Memetakan CRUD admin yang opt-in. Route diambil dari <see cref="NamedQueryAdminOptions"/>.
    /// Mapping kedua atau konflik pattern dengan endpoint lain langsung gagal saat startup.
    /// </summary>
    public static IEndpointRouteBuilder MapFunQueryNamedQueryAdmin(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider
            .GetRequiredService<NamedQueryAdminOptions>();
        var baseRoute = options.Route.TrimEnd('/');

        EnsureNoConflict(endpoints, baseRoute, "GET");
        EnsureNoConflict(endpoints, baseRoute + "/{key}", "GET");
        EnsureNoConflict(endpoints, baseRoute, "POST");
        EnsureNoConflict(endpoints, baseRoute + "/{key}", "PUT");
        EnsureNoConflict(endpoints, baseRoute + "/{key}", "PATCH");
        EnsureNoConflict(endpoints, baseRoute + "/{key}", "DELETE");

        endpoints.MapGet(baseRoute, ListAsync);
        endpoints.MapGet(baseRoute + "/{key}", GetAsync);
        endpoints.MapPost(baseRoute, CreateAsync);
        endpoints.MapPut(baseRoute + "/{key}", ReplaceAsync);
        endpoints.MapPatch(baseRoute + "/{key}", PatchAsync);
        endpoints.MapDelete(baseRoute + "/{key}", DeleteAsync);

        return endpoints;
    }

    private static async Task ListAsync(HttpContext context)
    {
        await RunAsync(context, async (service, authorizer, token) =>
        {
            if (!await authorizer.CanManageAsync(context, token))
            {
                await WriteErrorAsync(context, 403, "NAMED_QUERY_ADMIN_FORBIDDEN",
                    "Administrator access is required.");
                return;
            }

            var options = context.RequestServices
                .GetRequiredService<NamedQueryAdminOptions>();
            var limit = ReadInt(context, "limit", 50);
            var offset = ReadInt(context, "offset", 0);
            limit = Math.Clamp(limit, 1, options.MaxPageSize);
            offset = Math.Max(offset, 0);

            var items = await service.ListAsync(
                App(context),
                Tenant(context),
                limit,
                offset,
                token);

            await WriteJsonAsync(
                context,
                200,
                new NamedQueryListResponse(
                    items.Select(NamedQueryResponse.From).ToArray()));
        });
    }

    private static async Task GetAsync(HttpContext context)
    {
        await RunAsync(context, async (service, authorizer, token) =>
        {
            if (!await authorizer.CanManageAsync(context, token))
            {
                await WriteErrorAsync(context, 403, "NAMED_QUERY_ADMIN_FORBIDDEN",
                    "Administrator access is required.");
                return;
            }

            var key = Key(context);
            var entry = await service.GetAsync(
                App(context),
                Tenant(context),
                key,
                token);

            if (entry is null)
            {
                await WriteErrorAsync(context, 404, "UNKNOWN_NAMED_QUERY",
                    $"Named query '{key}' was not found.");
                return;
            }

            await WriteJsonAsync(context, 200, NamedQueryResponse.From(entry));
        });
    }

    private static async Task CreateAsync(HttpContext context)
    {
        await RunAsync(context, async (service, authorizer, token) =>
        {
            if (!await authorizer.CanManageAsync(context, token))
            {
                await WriteErrorAsync(context, 403, "NAMED_QUERY_ADMIN_FORBIDDEN",
                    "Administrator access is required.");
                return;
            }

            var request = await ReadBodyAsync(context, token);

            if (request is null)
                return;

            var entry = await service.CreateAsync(
                request,
                context.User.Identity?.Name,
                token);

            context.Response.Headers["ETag"] = $"\"{entry.Version}\"";
            await WriteJsonAsync(
                context,
                201,
                NamedQueryResponse.From(entry));
        });
    }

    private static Task ReplaceAsync(HttpContext context) =>
        UpdateAsync(context, patch: false);

    private static Task PatchAsync(HttpContext context) =>
        UpdateAsync(context, patch: true);

    private static async Task UpdateAsync(HttpContext context, bool patch)
    {
        await RunAsync(context, async (service, authorizer, token) =>
        {
            if (!await authorizer.CanManageAsync(context, token))
            {
                await WriteErrorAsync(context, 403, "NAMED_QUERY_ADMIN_FORBIDDEN",
                    "Administrator access is required.");
                return;
            }

            var request = await ReadBodyAsync(context, token);

            if (request is null)
                return;

            var expectedVersion = ReadVersion(context, request);

            if (expectedVersion is null)
            {
                await WriteErrorAsync(context, 400, "INVALID_REQUEST",
                    "If-Match or version is required.");
                return;
            }

            var key = Key(context);
            var entry = await service.UpdateAsync(
                App(context),
                Tenant(context),
                key,
                request,
                expectedVersion.Value,
                context.User.Identity?.Name,
                patch,
                token);

            if (entry is null)
            {
                await WriteErrorAsync(context, 404, "UNKNOWN_NAMED_QUERY",
                    $"Named query '{key}' was not found.");
                return;
            }

            context.Response.Headers["ETag"] = $"\"{entry.Version}\"";
            await WriteJsonAsync(context, 200, NamedQueryResponse.From(entry));
        });
    }

    private static async Task DeleteAsync(HttpContext context)
    {
        await RunAsync(context, async (service, authorizer, token) =>
        {
            if (!await authorizer.CanManageAsync(context, token))
            {
                await WriteErrorAsync(context, 403, "NAMED_QUERY_ADMIN_FORBIDDEN",
                    "Administrator access is required.");
                return;
            }

            var expectedVersion = ReadVersion(context, request: null);

            if (expectedVersion is null)
            {
                await WriteErrorAsync(context, 400, "INVALID_REQUEST",
                    "If-Match or version is required.");
                return;
            }

            var key = Key(context);
            var purge = bool.TryParse(
                context.Request.Query["purge"].FirstOrDefault(),
                out var parsed) && parsed;

            var deleted = await service.DeleteAsync(
                App(context),
                Tenant(context),
                key,
                expectedVersion.Value,
                hardDelete: purge,
                context.User.Identity?.Name,
                token);

            if (!deleted)
            {
                await WriteErrorAsync(context, 404, "UNKNOWN_NAMED_QUERY",
                    $"Named query '{key}' was not found.");
                return;
            }

            context.Response.StatusCode = 204;
        });
    }

    private static async Task RunAsync(
        HttpContext context,
        Func<INamedQueryAdminService, INamedQueryAdminAuthorizer,
            CancellationToken, Task> action)
    {
        var service = context.RequestServices
            .GetRequiredService<INamedQueryAdminService>();
        var authorizer = context.RequestServices
            .GetRequiredService<INamedQueryAdminAuthorizer>();

        try
        {
            await action(service, authorizer, context.RequestAborted);
        }
        catch (NamedQueryValidationException error)
        {
            await WriteErrorAsync(
                context,
                error.IsQueryError ? 422 : 400,
                "INVALID_NAMED_QUERY",
                error.Message);
        }
        catch (NamedQueryAlreadyExistsException error)
        {
            await WriteErrorAsync(context, 409, "NAMED_QUERY_EXISTS", error.Message);
        }
        catch (NamedQueryVersionConflictException error)
        {
            await WriteErrorAsync(context, 409, "NAMED_QUERY_CONFLICT", error.Message);
        }
        catch (NamedQueryNotFoundException error)
        {
            await WriteErrorAsync(context, 404, "UNKNOWN_NAMED_QUERY", error.Message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("FunQuery.NamedQueries.Admin")
                .LogError(error, "Named-query admin request failed.");

            await WriteErrorAsync(context, 500, "INTERNAL_ERROR",
                "The admin request could not be completed.");
        }
    }

    private static async Task<NamedQueryWriteRequest?> ReadBodyAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            await WriteErrorAsync(context, 413, "REQUEST_TOO_LARGE",
                "The admin request body is too large.");
            return null;
        }

        try
        {
            var request = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                NamedQueryJsonContext.Relaxed.NamedQueryWriteRequest,
                cancellationToken);

            if (request is null)
            {
                await WriteErrorAsync(context, 400, "INVALID_REQUEST",
                    "A JSON request body is required.");
            }

            return request;
        }
        catch (JsonException error)
        {
            await WriteErrorAsync(context, 400, "INVALID_REQUEST",
                $"The request body is not valid JSON: {error.Message}");
            return null;
        }
    }

    private static int? ReadVersion(
        HttpContext context,
        NamedQueryWriteRequest? request)
    {
        var header = context.Request.Headers["If-Match"].FirstOrDefault();

        if (header is not null)
        {
            var value = header.Trim().Trim('"');
            return int.TryParse(value, out var parsed) && parsed > 0
                ? parsed
                : null;
        }

        return request?.Version is > 0 ? request.Version : null;
    }

    private static int ReadInt(HttpContext context, string name, int fallback) =>
        int.TryParse(context.Request.Query[name].FirstOrDefault(), out var value)
            ? value
            : fallback;

    private static string App(HttpContext context) =>
        NormalizeScope(context.Request.Query["app"].FirstOrDefault());

    private static string Tenant(HttpContext context) =>
        NormalizeScope(context.Request.Query["tenant"].FirstOrDefault());

    private static string Key(HttpContext context) =>
        context.Request.RouteValues["key"]?.ToString() ?? string.Empty;

    private static string NormalizeScope(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "global" : value.Trim();

    private static Task WriteJsonAsync(
        HttpContext context,
        int status,
        NamedQueryResponse value) =>
        WriteJsonCoreAsync(
            context,
            status,
            value,
            NamedQueryJsonContext.Relaxed.NamedQueryResponse);

    private static Task WriteJsonAsync(
        HttpContext context,
        int status,
        NamedQueryListResponse value) =>
        WriteJsonCoreAsync(
            context,
            status,
            value,
            NamedQueryJsonContext.Relaxed.NamedQueryListResponse);

    private static async Task WriteJsonCoreAsync<T>(
        HttpContext context,
        int status,
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            value,
            typeInfo,
            context.RequestAborted);
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int status,
        string code,
        string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new NamedQueryAdminError(code, message),
            NamedQueryJsonContext.Relaxed.NamedQueryAdminError,
            context.RequestAborted);
    }

    private static void EnsureNoConflict(
        IEndpointRouteBuilder endpoints,
        string pattern,
        string method)
    {
        foreach (var dataSource in endpoints.DataSources)
        {
            foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
            {
                if (!string.Equals(
                        endpoint.RoutePattern.RawText,
                        pattern,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var methods = endpoint.Metadata
                    .GetMetadata<IHttpMethodMetadata>()
                    ?.HttpMethods;

                if (methods is not null &&
                    methods.Contains(method, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Cannot map the named-query admin endpoint " +
                        $"'{method} {pattern}': the route is already mapped.");
                }
            }
        }
    }
}
