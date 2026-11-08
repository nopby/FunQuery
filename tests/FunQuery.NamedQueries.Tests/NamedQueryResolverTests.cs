using FunQuery.Enums;

namespace FunQuery.NamedQueries.Tests;

public sealed class NamedQueryResolverTests
{
    private static NamedQueryEntry Entry(bool enabled = true) => new()
    {
        AppId = "app",
        TenantId = "tenant",
        Key = "orders",
        Query = "$source(@source).$count()",
        Source = "orders",
        Enabled = enabled,
    };

    [Fact]
    public async Task ResolvesAnEnabledEntry()
    {
        var store = new FakeNamedQueryStore();
        await store.CreateAsync(Entry(), CancellationToken.None);
        var resolver = new NamedQueryResolver(store);

        var result = await resolver.ResolveAsync(
            "app", "tenant", "orders", CancellationToken.None);

        Assert.Equal("orders", result.Key);
        Assert.Equal("$source(@source).$count()", result.Query);
        Assert.Equal("orders", result.SourceName);
    }

    [Fact]
    public async Task UnknownKeyIs404Code()
    {
        var resolver = new NamedQueryResolver(new FakeNamedQueryStore());

        var error = await Assert.ThrowsAsync<QueryException>(() =>
            resolver.ResolveAsync("app", "tenant", "missing", CancellationToken.None).AsTask());

        Assert.Equal(QueryErrorCode.UnknownNamedQuery, error.Code);
    }

    [Fact]
    public async Task DisabledEntryIsRejected()
    {
        var store = new FakeNamedQueryStore();
        await store.CreateAsync(Entry(enabled: false), CancellationToken.None);
        var resolver = new NamedQueryResolver(store);

        var error = await Assert.ThrowsAsync<QueryException>(() =>
            resolver.ResolveAsync("app", "tenant", "orders", CancellationToken.None).AsTask());

        Assert.Equal(QueryErrorCode.NamedQueryDisabled, error.Code);
    }
}
