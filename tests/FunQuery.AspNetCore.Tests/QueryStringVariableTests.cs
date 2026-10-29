using System.Globalization;
using System.Net;
using FunQuery.AspNetCore.Tests.Support;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FunQuery.AspNetCore.Tests;

/// <summary>
/// M5.4: query variables from the query string. Contract in docs/HttpApi.md.
/// </summary>
public class QueryStringVariableTests
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

    private static string Url(
        string query,
        string? source = null,
        params (string Key, string Value)[] variables)
    {
        var url = $"/query?q={Uri.EscapeDataString(query)}";

        if (source is not null)
            url += $"&source={Uri.EscapeDataString(source)}";

        foreach (var (key, value) in variables)
            url += $"&{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

        return url;
    }

    [Fact]
    public async Task AnIntVariableIsUsableInAPredicate()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([{id: 1}, {id: 2}, {id: 3}]).$filter(id gt @min)",
            variables: [("v.min", "2")]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[{\"id\":3}]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task JsonScalarsAreInferredWithTheLiteralRules()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$select({i: @i, l: @l, d: @d, s: @s, b: @b, n: @n})",
            variables:
            [
                ("v.i", "1"),
                ("v.l", "2147483648"),
                ("v.d", "1.5"),
                ("v.s", "Ana"),
                ("v.b", "true"),
                ("v.n", "null"),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "[{\"i\":1,\"l\":2147483648,\"d\":1.5,\"s\":\"Ana\",\"b\":true,\"n\":null}]",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BareAndQuotedStringsAreBothStrings()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var bare = await client.GetAsync(Url(
            "$source([1]).$map(@name)",
            variables: [("v.name", "Ana")]));

        var quoted = await client.GetAsync(Url(
            "$source([1]).$map(@name)",
            variables: [("v.name", "\"Ana\"")]));

        Assert.Equal("[\"Ana\"]", await bare.Content.ReadAsStringAsync());
        Assert.Equal("[\"Ana\"]", await quoted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AValueThatIsNotJsonIsAString()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$select({code: @code, text: @text})",
            variables: [("v.code", "00123"), ("v.text", "hello world")]));

        Assert.Equal(
            "[{\"code\":\"00123\",\"text\":\"hello world\"}]",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnEmptyValueIsAnEmptyString()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$map(@x)",
            variables: [("v.x", "")]));

        Assert.Equal("[\"\"]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ADateVariableCanBeConvertedWithDate()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([{d: '2024-01-01'}, {d: '2024-06-01'}])" +
            ".$filter($date(d) gt $date(@raw)).$count()",
            variables: [("v.raw", "2024-03-01")]));

        Assert.Equal("1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUndefinedVariableIsRejected()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$map(@missing)"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UNDEFINED_VARIABLE", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("v.")]
    [InlineData("v.1x")]
    [InlineData("v.a-b")]
    [InlineData("v.source")]
    public async Task InvalidNamesAreRejected(string key)
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$count()",
            variables: [(key, "1")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ADuplicateVariableIsRejected()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$count()",
            variables: [("v.x", "1"), ("v.x", "2")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TooManyVariablesAreRejected()
    {
        using var factory = CreateFactory(
            configure: options => options.Limits = new QueryLimits { MaxParameters = 2 });

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$count()",
            variables: [("v.a", "1"), ("v.b", "2"), ("v.c", "3")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VariablesCanBeDisabledWithAZeroLimit()
    {
        using var factory = CreateFactory(
            configure: options => options.Limits = new QueryLimits { MaxParameters = 0 });

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$count()",
            variables: [("v.x", "1")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AValueOverTheLengthLimitIsRejected()
    {
        using var factory = CreateFactory(
            configure: options =>
                options.Limits = new QueryLimits { MaxParameterValueLength = 3 });

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$count()",
            variables: [("v.x", "abcd")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("{\"a\":1}")]
    [InlineData("[1,2]")]
    public async Task ObjectAndArrayValuesAreRejected(string value)
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$count()",
            variables: [("v.x", value)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ANumberOutsideTheRangeIsRejected()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$source([1]).$count()",
            variables: [("v.x", "1e999")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("INVALID_PARAMETER", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LetCannotRedefineARequestVariable()
    {
        using var factory = CreateFactory();

        var response = await factory.CreateClient().GetAsync(Url(
            "$let(@min, 2).$source([1])",
            variables: [("v.min", "1")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VARIABLE_REDEFINED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VariablesAndASourceCoexist()
    {
        var source = new TestSource(
            "orders",
            TestSource.SchemaOf(("id", SemanticTypeOptions.Int)),
            TestSource.Row(("id", 1)),
            TestSource.Row(("id", 2)),
            TestSource.Row(("id", 3)));

        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(Url(
            "$source(@source).$filter(id gt @min).$count()",
            "orders",
            [("v.min", "1")]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task VariablesAreNotSharedBetweenConcurrentRequests()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var requests = Enumerable.Range(0, 20).Select(async value =>
        {
            var response = await client.GetAsync(Url(
                "$source([{id: 1}, {id: 2}, {id: 3}]).$filter(id gt @min).$count()",
                variables: [("v.min", value.ToString(CultureInfo.InvariantCulture))]));

            var body = await response.Content.ReadAsStringAsync();
            var expected = Enumerable.Range(1, 3)
                .Count(id => id > value)
                .ToString(CultureInfo.InvariantCulture);

            return (response.StatusCode, Expected: expected, Actual: body);
        });

        var results = await Task.WhenAll(requests);

        foreach (var result in results)
        {
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(result.Expected, result.Actual);
        }
    }
}
