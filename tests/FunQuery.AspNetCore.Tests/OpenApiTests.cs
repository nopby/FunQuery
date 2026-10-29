using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.AspNetCore.Tests;

/// <summary>M7.1: OpenAPI 3.1 document for the query endpoints.</summary>
public class OpenApiTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        Action<FunQueryOptions>? configure = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddFunQuery(configure);
                }));

    private static async Task<JsonDocument> GetDocument(
        HttpClient client,
        string path = "/openapi/v1.json")
    {
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void OpenApiIsDisabledByDefault()
    {
        Assert.False(new FunQueryOptions().EnableOpenApi);
    }

    [Fact]
    public async Task OpenApiDocumentDescribesBothMethods()
    {
        using var factory = CreateFactory(options => options.EnableOpenApi = true);
        using var document = await GetDocument(factory.CreateClient());

        var root = document.RootElement;

        Assert.Equal("3.1.0", root.GetProperty("openapi").GetString());

        var path = root.GetProperty("paths").GetProperty("/query");
        Assert.True(path.TryGetProperty("get", out _));
        Assert.True(path.TryGetProperty("post", out _));

        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("QueryRequest", out _));
        Assert.True(schemas.TryGetProperty("QueryErrorResponse", out _));

        var responses = path.GetProperty("get").GetProperty("responses");

        foreach (var code in new[] { "200", "400", "408", "413", "415", "422", "500" })
            Assert.True(responses.TryGetProperty(code, out _), code);
    }

    [Fact]
    public async Task OpenApiIsAbsentWhenDisabled()
    {
        using var factory = CreateFactory(options => options.EnableOpenApi = false);

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiReflectsConfiguredRouteAndParameters()
    {
        using var factory = CreateFactory(options =>
        {
            options.EnableOpenApi = true;
            options.Route = "/api/run";
            options.QueryParameter = "expr";
            options.SourceParameter = "src";
        });

        using var document = await GetDocument(factory.CreateClient());

        var path = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/run");

        var names = path.GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("expr", names);
        Assert.Contains("src", names);
    }

    [Fact]
    public async Task OpenApiRouteCanBeCustomized()
    {
        using var factory = CreateFactory(options =>
        {
            options.EnableOpenApi = true;
            options.OpenApiRoute = "/openapi/custom.json";
        });

        var client = factory.CreateClient();

        using var document = await GetDocument(client, "/openapi/custom.json");

        var defaultResponse = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, defaultResponse.StatusCode);
    }

    [Fact]
    public async Task OpenApiContainsNoSecrets()
    {
        using var factory = CreateFactory(options => options.EnableOpenApi = true);

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Password", body);
        Assert.DoesNotContain("Host=", body);
        Assert.DoesNotContain("SELECT", body);
    }
}
