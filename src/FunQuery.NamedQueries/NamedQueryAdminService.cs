using FunQuery.AspNetCore;
using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.NamedQueries;

public interface INamedQueryAdminService
{
    ValueTask<IReadOnlyList<NamedQueryEntry>> ListAsync(
        string appId,
        string tenantId,
        int limit,
        int offset,
        CancellationToken cancellationToken);

    ValueTask<NamedQueryEntry?> GetAsync(
        string appId,
        string tenantId,
        string key,
        CancellationToken cancellationToken);

    ValueTask<NamedQueryEntry> CreateAsync(
        NamedQueryWriteRequest request,
        string? updatedBy,
        CancellationToken cancellationToken);

    ValueTask<NamedQueryEntry?> UpdateAsync(
        string appId,
        string tenantId,
        string key,
        NamedQueryWriteRequest request,
        int expectedVersion,
        string? updatedBy,
        bool patch,
        CancellationToken cancellationToken);

    ValueTask<bool> DeleteAsync(
        string appId,
        string tenantId,
        string key,
        int expectedVersion,
        bool hardDelete,
        string? updatedBy,
        CancellationToken cancellationToken);
}

public sealed class NamedQueryAdminService : INamedQueryAdminService
{
    private readonly INamedQueryStore _store;
    private readonly SourceRegistry _sources;
    private readonly QueryEngine _engine;

    public NamedQueryAdminService(
        INamedQueryStore store,
        SourceRegistry sources,
        QueryEngine engine)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(engine);

        _store = store;
        _sources = sources;
        _engine = engine;
    }

    public ValueTask<IReadOnlyList<NamedQueryEntry>> ListAsync(
        string appId,
        string tenantId,
        int limit,
        int offset,
        CancellationToken cancellationToken) =>
        _store.ListAsync(appId, tenantId, limit, offset, cancellationToken);

    public ValueTask<NamedQueryEntry?> GetAsync(
        string appId,
        string tenantId,
        string key,
        CancellationToken cancellationToken) =>
        _store.GetAsync(appId, tenantId, key, cancellationToken);

    public async ValueTask<NamedQueryEntry> CreateAsync(
        NamedQueryWriteRequest request,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entry = new NamedQueryEntry
        {
            AppId = Normalize(request.AppId),
            TenantId = Normalize(request.TenantId),
            Key = request.Key ?? string.Empty,
            Query = request.Query ?? string.Empty,
            Source = NormalizeSource(request.Source),
            Enabled = request.Enabled ?? true,
            CacheMode = ParseMode(request.CacheMode),
            L1TtlSeconds = request.L1TtlSeconds,
            L2TtlSeconds = request.L2TtlSeconds,
            ParametersJson = request.ParametersJson,
            UpdatedBy = updatedBy,
        };

        Validate(entry);
        return await _store.CreateAsync(entry, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<NamedQueryEntry?> UpdateAsync(
        string appId,
        string tenantId,
        string key,
        NamedQueryWriteRequest request,
        int expectedVersion,
        string? updatedBy,
        bool patch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (expectedVersion < 1)
            throw new NamedQueryValidationException("version/If-Match is required.");

        var existing = await _store.GetAsync(appId, tenantId, key, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
            return null;

        if (!string.Equals(request.AppId ?? appId, appId, StringComparison.Ordinal) ||
            !string.Equals(request.TenantId ?? tenantId, tenantId, StringComparison.Ordinal) ||
            !string.Equals(request.Key ?? key, key, StringComparison.Ordinal))
        {
            throw new NamedQueryValidationException(
                "The route identity cannot be changed by the request body.");
        }

        var entry = existing with
        {
            Query = patch ? request.Query ?? existing.Query : request.Query ?? string.Empty,
            Source = patch
                ? request.Source is null
                    ? existing.Source
                    : NormalizeSource(request.Source)
                : NormalizeSource(request.Source),
            Enabled = request.Enabled ?? (patch ? existing.Enabled : true),
            CacheMode = request.CacheMode is null
                ? (patch ? existing.CacheMode : NamedQueryCacheMode.Default)
                : ParseMode(request.CacheMode),
            L1TtlSeconds = request.L1TtlSeconds ?? (patch ? existing.L1TtlSeconds : null),
            L2TtlSeconds = request.L2TtlSeconds ?? (patch ? existing.L2TtlSeconds : null),
            ParametersJson = request.ParametersJson ?? (patch ? existing.ParametersJson : null),
            UpdatedBy = updatedBy,
        };

        Validate(entry);

        var updated = await _store.UpdateAsync(entry, expectedVersion, cancellationToken)
            .ConfigureAwait(false);

        if (updated is null)
        {
            var current = await _store.GetAsync(
                appId,
                tenantId,
                key,
                cancellationToken).ConfigureAwait(false);

            if (current is null)
                return null;

            throw new NamedQueryVersionConflictException(
                $"Named query '{key}' is at version {current.Version}, " +
                $"not {expectedVersion}.");
        }

        return updated;
    }

    public async ValueTask<bool> DeleteAsync(
        string appId,
        string tenantId,
        string key,
        int expectedVersion,
        bool hardDelete,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        if (expectedVersion < 1)
            throw new NamedQueryValidationException("version/If-Match is required.");

        if (hardDelete)
            return await _store.DeleteAsync(
                appId,
                tenantId,
                key,
                expectedVersion,
                cancellationToken).ConfigureAwait(false);

        var existing = await _store.GetAsync(appId, tenantId, key, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
            return false;

        var updated = await _store.UpdateAsync(
            existing with { Enabled = false, UpdatedBy = updatedBy },
            expectedVersion,
            cancellationToken).ConfigureAwait(false);

        if (updated is null)
        {
            throw new NamedQueryVersionConflictException(
                $"Named query '{key}' changed before it could be disabled.");
        }

        return true;
    }

    private void Validate(NamedQueryEntry entry)
    {
        entry.Validate();

        IQuerySource? source = null;

        if (!string.IsNullOrWhiteSpace(entry.Source) &&
            !_sources.TryGet(entry.Source, out source))
        {
            throw new NamedQueryValidationException(
                $"Unknown source '{entry.Source}'.");
        }

        if (entry.Query.Contains("$named", StringComparison.Ordinal))
            throw new NamedQueryValidationException(
                "Nested $named is not supported in v1.", isQueryError: true);

        var environment = QueryEnvironment.Empty;

        if (source is not null)
        {
            environment = environment.With(
                "source",
                new QueryParameter(new ArrayType(source.Schema), null));
        }
        else if (entry.Query.Contains("$source(@source)", StringComparison.Ordinal))
        {
            throw new NamedQueryValidationException(
                "A named query using @source must declare source_name.", isQueryError: true);
        }

        try
        {
            _engine.Prepare(entry.Query, environment);
        }
        catch (QueryException error) when (
            error.Code == QueryErrorCode.UndefinedVariable)
        {
            // Variables are supplied at invocation time; syntax and known fields were
            // validated up to that point.
        }
        catch (QueryException error)
        {
            throw new NamedQueryValidationException(error.Message, isQueryError: true);
        }
    }

    private static NamedQueryCacheMode ParseMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "default" => NamedQueryCacheMode.Default,
            "none" => NamedQueryCacheMode.None,
            "custom" => NamedQueryCacheMode.Custom,
            _ => throw new NamedQueryValidationException(
                "cache_mode must be 'none', 'default', or 'custom'."),
        };

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "global" : value.Trim();

    private static string? NormalizeSource(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
