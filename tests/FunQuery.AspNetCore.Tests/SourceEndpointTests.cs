using System.Net;
using FunQuery.AspNetCore.Tests.Support;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FunQuery.AspNetCore.Tests;

/// <summary>
/// M5.3: registered sources, schema-driven analysis, and row materialization.
/// Contract in docs/Sources.md.
/// </summary>
public class SourceEndpointTests
{
    private static readonly ObjectType OrderSchema = TestSource.SchemaOf(
        ("id", SemanticTypeOptions.Int),
        ("customer", SemanticTypeOptions.String),
        ("total", SemanticTypeOptions.Decimal),
        ("orderedOn", SemanticTypeOptions.Date));

    private static TestSource Orders() => new(
        "orders",
        OrderSchema,
        TestSource.Row(
            ("id", 1), ("customer", "Ana"), ("total", 10.5m),
            ("orderedOn", new DateOnly(2024, 1, 15))),
        TestSource.Row(
            ("id", 2), ("customer", "Budi"), ("total", 20m),
            ("orderedOn", new DateOnly(2024, 2, 3))),
        TestSource.Row(
            ("id", 3), ("customer", "Citra"), ("total", 30m),
            ("orderedOn", new DateOnly(2024, 3, 21))));

    private static WebApplicationFactory<Program> CreateFactory(
        TestSource source,
        Action<FunQueryOptions>? configure = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddFunQuery(configure);
                    services.AddFunQuerySources(sources => sources.Add(source));
                }));

    private static string Url(string query, string? source = null) =>
        source is null
            ? $"/query?q={Uri.EscapeDataString(query)}"
            : $"/query?source={Uri.EscapeDataString(source)}&q={Uri.EscapeDataString(query)}";

    [Fact]
    public async Task ARegisteredSourceIsExecutedWithItsSchema()
    {
        var source = Orders();
        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$filter(id gt 1).$select(id, customer)", "orders"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "[{\"id\":2,\"customer\":\"Budi\"},{\"id\":3,\"customer\":\"Citra\"}]",
            await response.Content.ReadAsStringAsync());
        Assert.Equal(1, source.LoadCount);
    }

    [Fact]
    public async Task AnUnknownSourceIsRejected()
    {
        using var factory = CreateFactory(Orders());

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$count()", "missing"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UNKNOWN_SOURCE", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnUnknownFieldFailsBeforeTheSourceIsLoaded()
    {
        var source = Orders();
        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$filter(nope eq 1)", "orders"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UNKNOWN_IDENTIFIER", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, source.LoadCount);
    }

    [Fact]
    public async Task ASchemaTypeMismatchFailsBeforeTheSourceIsLoaded()
    {
        var source = Orders();
        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$filter(id eq 'x')", "orders"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("TYPE_MISMATCH", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, source.LoadCount);
    }

    [Fact]
    public async Task ARowThatDoesNotMatchTheSchemaIsRejected()
    {
        var source = new TestSource(
            "bad",
            TestSource.SchemaOf(("id", SemanticTypeOptions.Int)),
            TestSource.Row(("id", "not-an-int")));

        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source)", "bad"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("TYPE_MISMATCH", body);
        Assert.Contains("row 0", body);
        Assert.Contains("'id'", body);
        Assert.Equal(1, source.LoadCount);
    }

    [Fact]
    public async Task ASourceOverItsRowLimitIsRejected()
    {
        var source = Orders();
        using var factory = CreateFactory(
            source,
            options => options.Limits = new QueryLimits { MaxSourceItems = 2 });

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$count()", "orders"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("SOURCE_TOO_LARGE", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingFieldsReadAsNull()
    {
        var source = new TestSource(
            "people",
            TestSource.SchemaOf(
                ("id", SemanticTypeOptions.Int),
                ("nickname", SemanticTypeOptions.String)),
            TestSource.Row(("id", 1)));

        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$map(nickname)", "people"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[null]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SchemaDatesAreUsableInComparisons()
    {
        using var factory = CreateFactory(Orders());

        var response = await factory.CreateClient().GetAsync(
            Url(
                "$source(@source)" +
                ".$filter(orderedOn gt $date('2024-02-01'))" +
                ".$select(customer)",
                "orders"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "[{\"customer\":\"Budi\"},{\"customer\":\"Citra\"}]",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheRowShapeFollowsTheSchemaOrder()
    {
        using var factory = CreateFactory(Orders());

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$first()", "orders"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "{\"id\":1,\"customer\":\"Ana\",\"total\":10.5,\"orderedOn\":\"2024-01-15\"}",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnInlineSourceStillWorksWhenASourceIsRegistered()
    {
        var source = Orders();
        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            Url("$source([1, 2]).$count()"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, source.LoadCount);
    }

    [Fact]
    public async Task TheSourceParameterCannotBeRedefinedByLet()
    {
        var source = Orders();
        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            Url("$let(@source, 1).$source([1])", "orders"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("VARIABLE_REDEFINED", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, source.LoadCount);
    }
}
