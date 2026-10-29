using System.Net;
using FunQuery.AspNetCore.Tests.Support;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FunQuery.AspNetCore.Tests;

/// <summary>
/// M5.6: execution timeout, result-size limit, and request cancellation.
/// Contract in docs/HttpApi.md.
/// </summary>
public class ExecutionLimitTests
{
    private static WebApplicationFactory<Program> CreateFactory(
        IQuerySource? source = null,
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

    private static string Url(string query, string? source = null) =>
        source is null
            ? $"/query?q={Uri.EscapeDataString(query)}"
            : $"/query?source={Uri.EscapeDataString(source)}&q={Uri.EscapeDataString(query)}";

    [Fact]
    public async Task AQueryOverTheTimeLimitReturns408()
    {
        var rows = Enumerable.Range(0, 1_000)
            .Select(id => TestSource.Row(("id", id)))
            .ToArray();

        var source = new TestSource(
            "items",
            TestSource.SchemaOf(("id", SemanticTypeOptions.Int)),
            rows);

        using var factory = CreateFactory(
            source,
            options => options.Limits = new QueryLimits
            {
                MaxExecutionTime = TimeSpan.FromTicks(1),
            });

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$filter(id gt 0)", "items"));

        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);
        Assert.Contains(
            "EXECUTION_TIMEOUT",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AResultOverTheItemLimitReturns422()
    {
        using var factory = CreateFactory(
            configure: options =>
                options.Limits = new QueryLimits { MaxResultItems = 2 });

        var response = await factory.CreateClient().GetAsync(
            Url("$source([1, 2, 3])"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(
            "RESULT_TOO_LARGE",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AResultExactlyAtTheLimitSucceeds()
    {
        using var factory = CreateFactory(
            configure: options =>
                options.Limits = new QueryLimits { MaxResultItems = 2 });

        var response = await factory.CreateClient().GetAsync(
            Url("$source([1, 2])"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[1,2]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CancellingTheRequestAbortsTheQuery()
    {
        using var factory = CreateFactory(new BlockingSource());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            factory.CreateClient().GetAsync(
                Url("$source(@source).$count()", "blocked"),
                cancellation.Token));
    }
}
