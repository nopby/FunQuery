using FunQuery.AspNetCore;
using FunQuery.Enums;

namespace FunQuery.NamedQueries;

/// <summary>Implementasi resolver catalog untuk adapter HTTP.</summary>
public sealed class NamedQueryResolver : INamedQueryResolver
{
    private readonly INamedQueryStore _store;

    public NamedQueryResolver(INamedQueryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public async ValueTask<NamedQueryResolution> ResolveAsync(
        string appId,
        string tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new QueryException(
                QueryErrorCode.InvalidNamedQueryKey,
                "The named-query key must not be empty.");
        }

        NamedQueryEntry? entry;

        try
        {
            entry = await _store.GetAsync(
                appId,
                tenantId,
                key,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new QueryException(
                QueryErrorCode.NamedQueriesUnavailable,
                "The named-query catalog is unavailable.");
        }

        if (entry is null)
        {
            throw new QueryException(
                QueryErrorCode.UnknownNamedQuery,
                $"Named query '{key}' was not found.");
        }

        if (!entry.Enabled)
        {
            throw new QueryException(
                QueryErrorCode.NamedQueryDisabled,
                $"Named query '{key}' is disabled.");
        }

        return new NamedQueryResolution(
            entry.Key,
            entry.Query,
            entry.Source,
            entry.AppId,
            entry.TenantId,
            entry.Version)
        {
            CacheMode = entry.CacheMode switch
            {
                NamedQueryCacheMode.None => "none",
                NamedQueryCacheMode.Custom => "custom",
                _ => "default",
            },
            L1TtlSeconds = entry.L1TtlSeconds,
            L2TtlSeconds = entry.L2TtlSeconds,
        };
    }
}
