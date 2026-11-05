using System.Net;
using FunQuery.AspNetCore.Tests.Support;
using FunQuery.Caching;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.AspNetCore.Tests;

public sealed class QueryCacheTests
{
    private static WebApplicationFactory<Program> CreateFactory(TestSource source) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddFunQuery();
                    services.AddFunQuerySources(sources => sources.Add(source));
                    services.AddFunQueryQueryCache(options =>
                    {
                        options.Enabled = true;
                        options.LocalTtl = TimeSpan.FromMinutes(1);
                        options.DistributedTtl = TimeSpan.FromMinutes(2);
                    });
                }));

    private static TestSource Source() => new(
        "orders",
        TestSource.SchemaOf(("id", SemanticTypeOptions.Int)),
        TestSource.Row(("id", 1)),
        TestSource.Row(("id", 2)));

    private static string Url(string query, string? source = "orders") =>
        source is null
            ? $"/query?q={Uri.EscapeDataString(query)}"
            : $"/query?source={Uri.EscapeDataString(source)}" +
              $"&q={Uri.EscapeDataString(query)}";

    [Fact]
    public async Task TheSameRequestIsServedFromCache()
    {
        var source = Source();
        using var factory = CreateFactory(source);
        var client = factory.CreateClient();

        var first = await client.GetAsync(Url("$source(@source).$count()"));
        var second = await client.GetAsync(Url("$source(@source).$count()"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("2", await first.Content.ReadAsStringAsync());
        Assert.Equal("2", await second.Content.ReadAsStringAsync());
        Assert.Equal(1, source.LoadCount);
    }

    [Fact]
    public async Task DifferentVariablesUseDifferentCacheKeys()
    {
        var source = Source();
        using var factory = CreateFactory(source);
        var client = factory.CreateClient();

        var first = await client.GetAsync(
            Url("$source(@source).$filter(id gt @min).$count()") + "&v.min=1");
        var second = await client.GetAsync(
            Url("$source(@source).$filter(id gt @min).$count()") + "&v.min=2");

        Assert.Equal("1", await first.Content.ReadAsStringAsync());
        Assert.Equal("0", await second.Content.ReadAsStringAsync());
        Assert.Equal(2, source.LoadCount);
    }
}
