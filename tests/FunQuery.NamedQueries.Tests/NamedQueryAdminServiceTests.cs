using FunQuery.SemanticTypes;

namespace FunQuery.NamedQueries.Tests;

public sealed class NamedQueryAdminServiceTests
{
    private static NamedQueryAdminService Service(out FakeNamedQueryStore store)
    {
        store = new FakeNamedQueryStore();
        return new NamedQueryAdminService(store, TestSchema.Registry(), TestSchema.Engine());
    }

    private static NamedQueryWriteRequest Request(
        string key = "orders-over-100",
        string query = "$source(@source).$count()",
        string? source = "orders",
        string? cacheMode = null,
        int? l1 = null,
        int? l2 = null) => new()
    {
        AppId = "app",
        TenantId = "tenant",
        Key = key,
        Query = query,
        Source = source,
        CacheMode = cacheMode,
        L1TtlSeconds = l1,
        L2TtlSeconds = l2,
    };

    [Fact]
    public async Task CreateAndGetUseTheAppTenantScope()
    {
        var service = Service(out _);
        var created = await service.CreateAsync(
            Request(), "admin", CancellationToken.None);

        Assert.Equal(1, created.Version);
        Assert.Equal("app", created.AppId);
        Assert.Equal("tenant", created.TenantId);

        var loaded = await service.GetAsync(
            "app", "tenant", "orders-over-100", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("$source(@source).$count()", loaded.Query);
    }

    [Fact]
    public async Task UpdateIncrementsVersion()
    {
        var service = Service(out _);
        await service.CreateAsync(Request(), "admin", CancellationToken.None);

        var updated = await service.UpdateAsync(
            "app",
            "tenant",
            "orders-over-100",
            Request(query: "$source(@source).$select(id)"),
            1,
            "admin",
            patch: false,
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(2, updated.Version);
        Assert.Equal("$source(@source).$select(id)", updated.Query);
    }

    [Fact]
    public async Task StaleVersionRaisesAConflict()
    {
        var service = Service(out _);
        await service.CreateAsync(Request(), "admin", CancellationToken.None);
        await service.UpdateAsync(
            "app", "tenant", "orders-over-100",
            Request(query: "$source(@source).$select(id)"),
            1, "admin", false, CancellationToken.None);

        await Assert.ThrowsAsync<NamedQueryVersionConflictException>(() =>
            service.UpdateAsync(
                "app", "tenant", "orders-over-100",
                Request(query: "$source(@source).$select(total)"),
                1, "admin", false, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task SoftDeleteDisablesTheEntry()
    {
        var service = Service(out _);
        await service.CreateAsync(Request(), "admin", CancellationToken.None);

        var deleted = await service.DeleteAsync(
            "app", "tenant", "orders-over-100", 1, hardDelete: false,
            "admin", CancellationToken.None);

        Assert.True(deleted);
        var loaded = await service.GetAsync(
            "app", "tenant", "orders-over-100", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.False(loaded.Enabled);
        Assert.Equal(2, loaded.Version);
    }

    [Theory]
    [InlineData("$source(@source).$filter(nope eq 1).$count()")]
    [InlineData("$named('other')")]
    public async Task InvalidQueryIsRejected(string query)
    {
        var service = Service(out _);

        await Assert.ThrowsAsync<NamedQueryValidationException>(() =>
            service.CreateAsync(Request(query: query), "admin", CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task UnknownSourceIsRejected()
    {
        var service = Service(out _);

        await Assert.ThrowsAsync<NamedQueryValidationException>(() =>
            service.CreateAsync(
                Request(query: "$source(@source).$count()", source: "missing"),
                "admin",
                CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task SourceIsRequiredWhenQueryUsesTheBoundSource()
    {
        var service = Service(out _);

        await Assert.ThrowsAsync<NamedQueryValidationException>(() =>
            service.CreateAsync(
                Request(query: "$source(@source).$count()", source: null),
                "admin",
                CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task CustomCacheModeRequiresBothTtls()
    {
        var service = Service(out _);

        await Assert.ThrowsAsync<NamedQueryValidationException>(() =>
            service.CreateAsync(
                Request(cacheMode: "custom", l1: 2),
                "admin",
                CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task CustomCacheModeAcceptsValidTtls()
    {
        var service = Service(out _);

        var created = await service.CreateAsync(
            Request(cacheMode: "custom", l1: 2, l2: 30),
            "admin",
            CancellationToken.None);

        Assert.Equal(NamedQueryCacheMode.Custom, created.CacheMode);
        Assert.Equal(2, created.L1TtlSeconds);
        Assert.Equal(30, created.L2TtlSeconds);
    }

    [Fact]
    public async Task UndefinedVariablesAreAccepted()
    {
        var service = Service(out _);

        var created = await service.CreateAsync(
            Request(query: "$source(@source).$filter(total gt @min).$count()"),
            "admin",
            CancellationToken.None);

        Assert.Equal("orders-over-100", created.Key);
    }

    [Fact]
    public async Task InvalidKeyIsRejected()
    {
        var service = Service(out _);

        await Assert.ThrowsAsync<NamedQueryValidationException>(() =>
            service.CreateAsync(
                Request(key: "../bad"),
                "admin",
                CancellationToken.None).AsTask());
    }
}
