namespace FunQuery.NamedQueries;

/// <summary>Penyimpanan named query. Implementasi PostgreSQL ada di project ini.</summary>
public interface INamedQueryStore
{
    ValueTask<NamedQueryEntry?> GetAsync(
        string appId,
        string tenantId,
        string key,
        CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<NamedQueryEntry>> ListAsync(
        string appId,
        string tenantId,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    ValueTask<NamedQueryEntry> CreateAsync(
        NamedQueryEntry entry,
        CancellationToken cancellationToken);

    /// <summary>Update dengan optimistic concurrency; null berarti versi/entry tidak cocok.</summary>
    ValueTask<NamedQueryEntry?> UpdateAsync(
        NamedQueryEntry entry,
        int expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Hard delete; false berarti versi/entry tidak cocok.</summary>
    ValueTask<bool> DeleteAsync(
        string appId,
        string tenantId,
        string key,
        int expectedVersion,
        CancellationToken cancellationToken);
}
