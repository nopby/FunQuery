using System.Net;
using FunQuery.Enums;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.AspNetCore.Tests;

/// <summary>
/// M6.3: a source with an executor is handled by its provider, not loaded into memory.
/// </summary>
public class ProviderSelectionTests
{
    private static WebApplicationFactory<Program> CreateFactory(IQuerySource source) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.AddFunQuery();
                    services.AddFunQuerySources(sources => sources.Add(source));
                }));

    private static string Url(string query, string source) =>
        $"/query?source={Uri.EscapeDataString(source)}&q={Uri.EscapeDataString(query)}";

    [Fact]
    public async Task ASourceWithAnExecutorIsExecutedByTheProvider()
    {
        var source = new FakeSource(new FakeExecutor(42L));
        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$count()", "fake"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("42", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, source.LoadCount);
    }

    [Fact]
    public async Task ProviderErrorsUseTheStructuredEnvelope()
    {
        var source = new FakeSource(
            new FakeExecutor(error: QueryErrorCode.NotSupported));

        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            Url("$source(@source).$count()", "fake"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("NOT_SUPPORTED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InlineSourcesStillUseTheInMemoryPath()
    {
        var source = new FakeSource(new FakeExecutor(42L));
        using var factory = CreateFactory(source);

        var response = await factory.CreateClient().GetAsync(
            "/query?q=" + Uri.EscapeDataString("$source([1, 2]).$count()"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, source.LoadCount);
    }

    private sealed class FakeSource : IQuerySource
    {
        private readonly IQuerySourceExecutor _executor;

        public FakeSource(IQuerySourceExecutor executor)
        {
            _executor = executor;
        }

        public string Name => "fake";

        public ObjectType Schema { get; } = new(
            new Dictionary<string, SemanticType>(StringComparer.Ordinal)
            {
                ["id"] = SemanticTypeOptions.Int,
            });

        public SourceCapabilities Capabilities => SourceCapabilities.All;

        public IQuerySourceExecutor Executor => _executor;

        public int LoadCount { get; private set; }

        public ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(
            CancellationToken cancellationToken)
        {
            LoadCount++;
            return ValueTask.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
                Array.Empty<IReadOnlyDictionary<string, object?>>());
        }
    }

    private sealed class FakeExecutor : IQuerySourceExecutor
    {
        private readonly object? _result;
        private readonly QueryErrorCode? _error;

        public FakeExecutor(object? result = null, QueryErrorCode? error = null)
        {
            _result = result;
            _error = error;
        }

        public ValueTask<object?> ExecuteAsync(
            QueryPlan plan,
            QueryEnvironment environment,
            CancellationToken cancellationToken = default)
        {
            if (_error is { } code)
                throw new QueryException(code, "Provider failure.");

            return ValueTask.FromResult(_result);
        }
    }
}
