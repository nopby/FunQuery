using System.Net;
using System.Text;
using System.Text.Json;
using FunQuery.AspNetCore.Tests.Support;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FunQuery.AspNetCore.Tests;

/// <summary>
/// M5.5: POST /query and the JSON request contract. Contract in docs/HttpApi.md.
/// </summary>
public class PostEndpointTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        TestSource? source = null,
        Action<FunQueryOptions>? configure = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddFunQuery(configure);
                    services.AddFunQuerySources(sources =>
                    {
                        if (source is not null)
                            sources.Add(source);
                    });
                }));

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    private static string Body(
        string query,
        string? source = null,
        string? variablesJson = null)
    {
        var json = $"\"query\":{JsonSerializer.Serialize(query)}";

        if (source is not null)
            json += $",\"source\":{JsonSerializer.Serialize(source)}";

        if (variablesJson is not null)
            json += $",\"variables\":{variablesJson}";

        return "{" + json + "}";
    }

    private static string GetUrl(
        string query,
        params (string Key, string Value)[] variables)
    {
        var url = $"/query?q={Uri.EscapeDataString(query)}";

        foreach (var (key, value) in variables)
            url += $"&{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

        return url;
    }

    [Fact]
    public async Task PostMatchesGetForTheSameQueryAndVariables()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        const string query =
            "$source([{id: 1}, {id: 2}, {id: 3}])" +
            ".$filter(id gt @min).$select(id)";

        var get = await client.GetAsync(GetUrl(query, ("v.min", "1")));
        var post = await client.PostAsync(
            "/query",
            Json(Body(query, variablesJson: "{\"min\":1}")));

        Assert.Equal(get.StatusCode, post.StatusCode);
        Assert.Equal(
            await get.Content.ReadAsStringAsync(),
            await post.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostUsesASourceAndVariables()
    {
        var source = new TestSource(
            "orders",
            TestSource.SchemaOf(("id", SemanticTypeOptions.Int)),
            TestSource.Row(("id", 1)),
            TestSource.Row(("id", 2)),
            TestSource.Row(("id", 3)));

        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body(
                "$source(@source).$filter(id gt @min).$count()",
                "orders",
                "{\"min\":1}")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MalformedJsonIsRejected()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json("{\"query\":"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_REQUEST", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AContentTypeThatIsNotJsonIsRejected()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            new StringContent("{}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Contains(
            "UNSUPPORTED_MEDIA_TYPE",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ABodyOverTheLimitIsRejected()
    {
        using var factory = CreateFactory(
            configure: options => options.MaxRequestBodyBytes = 100);

        var body = Body(
            "$source([1]).$count()",
            variablesJson: $"{{\"x\":\"{new string('a', 200)}\"}}");

        var response = await factory.CreateClient().PostAsync("/query", Json(body));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("REQUEST_TOO_LARGE", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AMissingQueryFieldIsRejected()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync("/query", Json("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_REQUEST", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NullVariablesAreAllowed()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json("{\"query\":\"$source([1]).$count()\",\"variables\":null}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VariablesMustBeAnObject()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json("{\"query\":\"$source([1]).$count()\",\"variables\":[1,2]}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_REQUEST", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("{\"x\":[1,2]}")]
    [InlineData("{\"x\":{\"a\":1}}")]
    public async Task ArrayAndObjectVariablesAreRejected(string variablesJson)
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body(
                "$source([1]).$count()",
                variablesJson: variablesJson)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NumberTypesFollowTheLiteralRules()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body(
                "$source([1]).$select({i: @i, l: @l, d: @d})",
                variablesJson: "{\"i\":1,\"l\":2147483648,\"d\":1.5}")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "[{\"i\":1,\"l\":2147483648,\"d\":1.5}]",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnInvalidVariableNameIsRejected()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body(
                "$source([1]).$count()",
                variablesJson: "{\"a-b\":1}")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ADuplicateVariableNameIsRejected()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body(
                "$source([1]).$count()",
                variablesJson: "{\"x\":1,\"x\":2}")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TooManyVariablesAreRejected()
    {
        using var factory = CreateFactory(
            configure: options => options.Limits = new QueryLimits { MaxParameters = 2 });

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body(
                "$source([1]).$count()",
                variablesJson: "{\"a\":1,\"b\":2,\"c\":3}")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AValueOverTheLengthLimitIsRejected()
    {
        using var factory = CreateFactory(
            configure: options =>
                options.Limits = new QueryLimits { MaxParameterValueLength = 3 });

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body(
                "$source([1]).$count()",
                variablesJson: "{\"x\":\"abcd\"}")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LetCannotRedefineARequestVariable()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body(
                "$let(@min, 2).$source([1])",
                variablesJson: "{\"min\":1}")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VARIABLE_REDEFINED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ALongQuerySucceedsThroughPost()
    {
        using var factory = CreateFactory();

        var query = $"$source(['{new string('a', 1500)}']).$count()";

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body(query)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnknownSourceIsRejected()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json(Body("$source(@source).$count()", "missing")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UNKNOWN_SOURCE", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownTopLevelPropertiesAreIgnored()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json("{\"query\":\"$source([1]).$count()\",\"extra\":true}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheQueryFieldMustBeAString()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().PostAsync(
            "/query",
            Json("{\"query\":1}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_REQUEST", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostAndGetReturnTheSameErrorEnvelope()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var get = await client.GetAsync(GetUrl("$unknown()"));
        var post = await client.PostAsync("/query", Json(Body("$unknown()")));

        Assert.Equal(get.StatusCode, post.StatusCode);
        Assert.Equal(
            await get.Content.ReadAsStringAsync(),
            await post.Content.ReadAsStringAsync());
    }
}
