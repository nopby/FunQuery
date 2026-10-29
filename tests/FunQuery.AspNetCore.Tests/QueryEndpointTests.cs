using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FunQuery.AspNetCore.Tests;

/// <summary>
/// M5.1: the GET endpoint executes a query through the adapter and returns raw JSON.
/// Error shapes are finalized in M5.2.
/// </summary>
public class QueryEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public QueryEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private static string Url(string query) =>
        $"/query?q={Uri.EscapeDataString(query)}";

    [Fact]
    public async Task Get_ExecutesQueryAndReturnsJson()
    {
        var response = await _client.GetAsync(Url("$source([1, 2, 3]).$count()"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("3", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_ReturnsTheSameJsonAsTheEngine()
    {
        const string query =
            "$source([{id: 1, name: 'a'}, {id: 2, name: 'b'}]).$select(id, name)";

        var expected = new QueryEngine().Execute(query).ToJson();

        var response = await _client.GetAsync(Url(query));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_SupportsComplexQueries()
    {
        const string query =
            "$source([{id: 1, name: 'Ana'}, {id: 2, name: 'Bob'}])" +
            ".$filter($lower(name) eq 'ana')" +
            ".$select({label: $upper(name)})";

        var response = await _client.GetAsync(Url(query));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[{\"label\":\"ANA\"}]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_MissingQuery_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/query");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("INVALID_REQUEST", body);
    }

    [Fact]
    public async Task Get_EmptyQuery_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/query?q=%20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_REQUEST", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_InvalidQuery_ReturnsStructuredError()
    {
        var response = await _client.GetAsync(Url("$unknown()"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"UNKNOWN_FUNCTION\"", body);
        Assert.Contains("\"message\"", body);
        Assert.Contains("\"position\":0", body);
    }

    [Fact]
    public async Task Get_ParserError_ReturnsStructuredError()
    {
        var response = await _client.GetAsync(Url("$source([1, 2,])"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"UNEXPECTED_TOKEN\"", body);
        Assert.Contains("\"position\":", body);
    }

    [Fact]
    public async Task Get_QueryTooLong_Returns413()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                    services.AddFunQuery(options =>
                        options.Limits = new QueryLimits { MaxInputLength = 10 })));

        var response = await factory.CreateClient()
            .GetAsync(Url("$source([1, 2, 3]).$count()"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("INPUT_TOO_LONG", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_TooManyTokens_Returns400()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                    services.AddFunQuery(options =>
                        options.Limits = new QueryLimits { MaxTokens = 2 })));

        var response = await factory.CreateClient().GetAsync(Url("$source([1])"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("TOO_MANY_TOKENS", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_TooDeep_Returns400()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                    services.AddFunQuery(options =>
                        options.Limits = new QueryLimits { MaxDepth = 2 })));

        var response = await factory.CreateClient().GetAsync(Url("$source([[1]])"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MAX_DEPTH_EXCEEDED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_UsesTheConfiguredRouteAndParameter()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                    services.AddFunQuery(options =>
                    {
                        options.Route = "/api/run";
                        options.QueryParameter = "expr";
                    })));

        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/run?expr={Uri.EscapeDataString("$source([1]).$count()")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", await response.Content.ReadAsStringAsync());
    }
}
