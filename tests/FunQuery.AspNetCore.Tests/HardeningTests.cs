using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FunQuery.AspNetCore.Tests.Support;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FunQuery.AspNetCore.Tests;

/// <summary>
/// M5.7: concurrency, no cross-request state, and the security review checklist.
/// </summary>
public class HardeningTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        Action<FunQueryOptions>? configure = null,
        params IQuerySource[] sources) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddFunQuery(configure);
                    services.AddFunQuerySources(registry =>
                    {
                        foreach (var source in sources)
                            registry.Add(source);
                    });
                }));

    private static TestSource Ids(string name, params int[] ids) =>
        new(
            name,
            TestSource.SchemaOf(("id", SemanticTypeOptions.Int)),
            ids.Select(id => TestSource.Row(("id", id))).ToArray());

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
    public async Task ConcurrentRequestsUseTheirOwnSourceAndVariables()
    {
        var orders = Ids("orders", 1, 2, 3);
        var people = Ids("people", 10, 11, 12);

        using var factory = CreateFactory(null, orders, people);
        var client = factory.CreateClient();

        var requests = Enumerable.Range(0, 20).Select(async value =>
        {
            var useOrders = value % 2 == 0;
            var source = useOrders ? "orders" : "people";
            var min = useOrders ? 1 : 10;

            var response = await client.GetAsync(Url(
                "$source(@source).$filter(id gt @min).$count()",
                source,
                ("v.min", min.ToString(CultureInfo.InvariantCulture))));

            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        });

        var results = await Task.WhenAll(requests);

        foreach (var result in results)
        {
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal("2", result.Body);
        }

        Assert.Equal(10, orders.LoadCount);
        Assert.Equal(10, people.LoadCount);
    }

    [Fact]
    public async Task ErrorBodiesContainNoStackTraceOrServerPath()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync(Url("$unknown()"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("   at ", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain(":\\", body);
    }

    [Fact]
    public async Task AnErrorNeverEchoesTheRequestBody()
    {
        const string marker = "secret-marker-42";

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Malformed on purpose, with the marker inside the raw body.
        var body = $"{{\"query\":\"{marker}\",";

        var response = await client.PostAsync(
            "/query",
            new StringContent(body, Encoding.UTF8, "application/json"));

        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(marker, text);
    }

    [Fact]
    public async Task AnUnknownSourceDoesNotListTheRegisteredSources()
    {
        using var factory = CreateFactory(
            null,
            Ids("orders", 1),
            Ids("people", 2));

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$count()", "missing"));

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("UNKNOWN_SOURCE", body);
        Assert.DoesNotContain("orders", body);
        Assert.DoesNotContain("people", body);
    }

    [Fact]
    public async Task PostAlsoEnforcesTheQueryInputLimit()
    {
        using var factory = CreateFactory(
            options => options.Limits = new QueryLimits { MaxInputLength = 10 });

        var body = JsonSerializer.Serialize(new
        {
            query = "$source([1, 2, 3]).$count()",
        });

        var response = await factory.CreateClient().PostAsync(
            "/query",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("INPUT_TOO_LONG", await response.Content.ReadAsStringAsync());
    }
}
