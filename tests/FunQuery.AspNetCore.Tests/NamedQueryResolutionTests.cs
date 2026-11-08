using System.Net;
using System.Net.Http.Json;
using FunQuery.AspNetCore.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.AspNetCore.Tests;

public sealed class NamedQueryResolutionTests
{
    private static TestSource Source() => new(
        "orders",
        TestSource.SchemaOf(("id", FunQuery.SemanticTypes.SemanticTypeOptions.Int)),
        TestSource.Row(("id", 1)),
        TestSource.Row(("id", 2)));

    private static WebApplicationFactory<Program> CreateFactory(
        TestSource source,
        INamedQueryResolver? resolver = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddFunQuery();
                    services.AddFunQuerySources(sources => sources.Add(source));

                    if (resolver is not null)
                        services.AddSingleton(resolver);
                }));

    private static FakeResolver Resolver() => new(
        new NamedQueryResolution(
            "orders",
            "$source(@source).$filter(id gt @min).$count()",
            "orders",
            "global",
            "global",
            1));

    private static string Url(string? query, string? key = null, string? source = null)
    {
        var parts = new List<string>();
        if (key is not null) parts.Add($"key={Uri.EscapeDataString(key)}");
        if (query is not null) parts.Add($"q={Uri.EscapeDataString(query)}");
        if (source is not null) parts.Add($"source={Uri.EscapeDataString(source)}");
        return "/query?" + string.Join("&", parts);
    }

    [Fact]
    public async Task KeyAndNamedResolveToTheSameStoredQuery()
    {
        var source = Source();
        using var factory = CreateFactory(source, Resolver());
        var client = factory.CreateClient();

        var byKey = await client.GetAsync(Url(null, "orders") + "&v.min=1");
        var byFunction = await client.GetAsync(Url("$named('orders')") + "&v.min=1");

        Assert.Equal(HttpStatusCode.OK, byKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byFunction.StatusCode);
        Assert.Equal("1", await byKey.Content.ReadAsStringAsync());
        Assert.Equal("1", await byFunction.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostKeyUsesTheSameResolution()
    {
        using var factory = CreateFactory(Source(), Resolver());
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/query", new
        {
            key = "orders",
            variables = new { min = 1 },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task KeyAndQueryTogetherAreRejected()
    {
        using var factory = CreateFactory(Source(), Resolver());
        var client = factory.CreateClient();

        var response = await client.GetAsync(Url("$source([1]).$count()", "orders"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SourceWithKeyIsRejected()
    {
        using var factory = CreateFactory(Source(), Resolver());
        var client = factory.CreateClient();

        var response = await client.GetAsync(Url(null, "orders", "orders"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownKeyReturns404()
    {
        using var factory = CreateFactory(Source(), Resolver());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/query?key=missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MissingResolverReturns503()
    {
        using var factory = CreateFactory(Source());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/query?key=orders");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task NamedWithSourceIsRejected()
    {
        using var factory = CreateFactory(Source(), Resolver());
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            Url("$named('orders')", source: "orders"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class FakeResolver(params NamedQueryResolution[] entries)
        : INamedQueryResolver
    {
        private readonly Dictionary<string, NamedQueryResolution> _entries =
            entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);

        public ValueTask<NamedQueryResolution> ResolveAsync(
            string appId,
            string tenantId,
            string key,
            CancellationToken cancellationToken)
        {
            if (_entries.TryGetValue(key, out var entry))
                return ValueTask.FromResult(entry);

            throw new FunQuery.QueryException(
                FunQuery.Enums.QueryErrorCode.UnknownNamedQuery,
                $"Named query '{key}' was not found.");
        }
    }
}
