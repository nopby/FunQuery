using Microsoft.Extensions.Caching.Memory;

namespace FunQuery.NamedQueries;

public interface INamedQueryCacheInvalidator
{
    void Invalidate(string appId, string tenantId, string key);
}

/// <summary>Decorator lookup-cache untuk catalog; NOTIFY tetap menjadi invalidasi utama.</summary>
public sealed class NamedQueryLookupCache :
    INamedQueryStore,
    INamedQueryCacheInvalidator
{
    private const string Prefix = "fq:nq:";
    private readonly INamedQueryStore _inner;
    private readonly IMemoryCache _cache;
    private readonly NamedQueryStoreOptions _options;

    public NamedQueryLookupCache(
        INamedQueryStore inner,
        IMemoryCache cache,
        NamedQueryStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);

        _inner = inner;
        _cache = cache;
        _options = options;
    }

    public async ValueTask<NamedQueryEntry?> GetAsync(
        string appId,
        string tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        var cacheKey = Key(appId, tenantId, key);

        if (_cache.TryGetValue(cacheKey, out NamedQueryEntry? cached))
            return cached;

        var entry = await _inner.GetAsync(
            appId,
            tenantId,
            key,
            cancellationToken).ConfigureAwait(false);

        if (entry is not null)
        {
            _cache.Set(
                cacheKey,
                entry,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _options.LookupCacheTtl,
                    Size = 1,
                });
        }

        return entry;
    }

    public ValueTask<IReadOnlyList<NamedQueryEntry>> ListAsync(
        string appId,
        string tenantId,
        int limit,
        int offset,
        CancellationToken cancellationToken) =>
        _inner.ListAsync(appId, tenantId, limit, offset, cancellationToken);

    public async ValueTask<NamedQueryEntry> CreateAsync(
        NamedQueryEntry entry,
        CancellationToken cancellationToken)
    {
        var created = await _inner.CreateAsync(entry, cancellationToken)
            .ConfigureAwait(false);

        Invalidate(entry.AppId, entry.TenantId, entry.Key);
        return created;
    }

    public async ValueTask<NamedQueryEntry?> UpdateAsync(
        NamedQueryEntry entry,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var updated = await _inner.UpdateAsync(entry, expectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (updated is not null)
            Invalidate(entry.AppId, entry.TenantId, entry.Key);

        return updated;
    }

    public async ValueTask<bool> DeleteAsync(
        string appId,
        string tenantId,
        string key,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var deleted = await _inner.DeleteAsync(
            appId,
            tenantId,
            key,
            expectedVersion,
            cancellationToken).ConfigureAwait(false);

        if (deleted)
            Invalidate(appId, tenantId, key);

        return deleted;
    }

    public void Invalidate(string appId, string tenantId, string key) =>
        _cache.Remove(Key(appId, tenantId, key));

    private static string Key(string appId, string tenantId, string key) =>
        Prefix + appId + "\0" + tenantId + "\0" + key;
}
