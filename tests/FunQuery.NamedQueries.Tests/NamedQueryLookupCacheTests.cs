using Microsoft.Extensions.Caching.Memory;

namespace FunQuery.NamedQueries.Tests;

public sealed class NamedQueryLookupCacheTests
{
    private static NamedQueryEntry Entry(string key = "orders") => new()
    {
        AppId = "app",
        TenantId = "tenant",
        Key = key,
        Query = "$source(@source).$count()",
        Source = "orders",
        UpdatedBy = "admin",
    };

    [Fact]
    public async Task SecondLookupComesFromMemory()
    {
        var inner = new FakeNamedQueryStore();
        await inner.CreateAsync(Entry(), CancellationToken.None);

        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = new NamedQueryLookupCache(
            inner,
            memory,
            new NamedQueryStoreOptions { ConnectionString = "unused" });

        await cache.GetAsync("app", "tenant", "orders", CancellationToken.None);
        await cache.GetAsync("app", "tenant", "orders", CancellationToken.None);

        Assert.Equal(1, inner.GetCalls);
    }

    [Fact]
    public async Task InvalidateForcesTheNextLookupToHitTheStore()
    {
        var inner = new FakeNamedQueryStore();
        await inner.CreateAsync(Entry(), CancellationToken.None);

        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = new NamedQueryLookupCache(
            inner,
            memory,
            new NamedQueryStoreOptions { ConnectionString = "unused" });

        await cache.GetAsync("app", "tenant", "orders", CancellationToken.None);
        cache.Invalidate("app", "tenant", "orders");
        await cache.GetAsync("app", "tenant", "orders", CancellationToken.None);

        Assert.Equal(2, inner.GetCalls);
    }

    [Fact]
    public async Task DifferentScopesDoNotShareEntries()
    {
        var inner = new FakeNamedQueryStore();
        await inner.CreateAsync(Entry(), CancellationToken.None);
        await inner.CreateAsync(
            Entry() with { TenantId = "tenant-2", Query = "$source(@source).$select(id)" },
            CancellationToken.None);

        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = new NamedQueryLookupCache(
            inner,
            memory,
            new NamedQueryStoreOptions { ConnectionString = "unused" });

        var first = await cache.GetAsync("app", "tenant", "orders", CancellationToken.None);
        var second = await cache.GetAsync("app", "tenant-2", "orders", CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Query, second.Query);
    }
}
