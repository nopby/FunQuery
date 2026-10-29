using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FunQuery.Server.Playground;

public static class PlaygroundEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Memetakan <c>GET /playground/config</c>. Endpoint ini hanya dipanggil host ketika playground
    /// diaktifkan, dan memakai <see cref="RequestDelegate"/> agar tetap kompatibel dengan Native AOT.
    /// </summary>
    public static IEndpointRouteBuilder MapFunQueryPlayground(
        this IEndpointRouteBuilder endpoints,
        PlaygroundOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(options);

        RequestDelegate config = context => WriteConfigAsync(context, options);
        endpoints.MapGet("/playground/config", config);

        return endpoints;
    }

    private static async Task WriteConfigAsync(
        HttpContext context,
        PlaygroundOptions options)
    {
        var sources = options.ExposeSourceNames
            ? options.SourceNames
            : Array.Empty<string>();

        var examples = options.ExposeSourceNames
            ? options.Examples
            : options.Examples.Where(example => example.Source is null).ToArray();

        var config = new PlaygroundConfig(options.ExposeSourceNames, sources, examples);
        var json = JsonSerializer.Serialize(
            config,
            PlaygroundJsonContext.Default.PlaygroundConfig);

        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(json, context.RequestAborted);
    }
}
