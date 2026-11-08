using FunQuery.AspNetCore;
using FunQuery.SemanticTypes;

namespace FunQuery.NamedQueries.Tests;

internal sealed class TestSource : IQuerySource
{
    public TestSource(string name, ObjectType schema)
    {
        Name = name;
        Schema = schema;
    }

    public string Name { get; }

    public ObjectType Schema { get; }

    public SourceCapabilities Capabilities => SourceCapabilities.All;

    public ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
            Array.Empty<IReadOnlyDictionary<string, object?>>());
}

internal sealed class FakeNamedQueryStore : INamedQueryStore
{
    private readonly Dictionary<string, NamedQueryEntry> _entries =
        new(StringComparer.Ordinal);

    public int GetCalls { get; private set; }

    public ValueTask<NamedQueryEntry?> GetAsync(
        string appId,
        string tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        GetCalls++;
        _entries.TryGetValue(Key(appId, tenantId, key), out var entry);
        return ValueTask.FromResult(entry);
    }

    public ValueTask<IReadOnlyList<NamedQueryEntry>> ListAsync(
        string appId,
        string tenantId,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var items = _entries.Values
            .Where(entry =>
                entry.AppId == appId && entry.TenantId == tenantId)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Skip(offset)
            .Take(limit)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<NamedQueryEntry>>(items);
    }

    public ValueTask<NamedQueryEntry> CreateAsync(
        NamedQueryEntry entry,
        CancellationToken cancellationToken)
    {
        var key = Key(entry.AppId, entry.TenantId, entry.Key);

        if (!_entries.TryAdd(key, entry))
            throw new NamedQueryAlreadyExistsException("already exists.");

        return ValueTask.FromResult(entry);
    }

    public ValueTask<NamedQueryEntry?> UpdateAsync(
        NamedQueryEntry entry,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var key = Key(entry.AppId, entry.TenantId, entry.Key);

        if (!_entries.TryGetValue(key, out var existing) ||
            existing.Version != expectedVersion)
        {
            return ValueTask.FromResult<NamedQueryEntry?>(null);
        }

        var updated = entry with { Version = expectedVersion + 1 };
        _entries[key] = updated;
        return ValueTask.FromResult<NamedQueryEntry?>(updated);
    }

    public ValueTask<bool> DeleteAsync(
        string appId,
        string tenantId,
        string key,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var mapKey = Key(appId, tenantId, key);

        if (!_entries.TryGetValue(mapKey, out var existing) ||
            existing.Version != expectedVersion)
        {
            return ValueTask.FromResult(false);
        }

        _entries.Remove(mapKey);
        return ValueTask.FromResult(true);
    }

    private static string Key(string appId, string tenantId, string key) =>
        appId + "\0" + tenantId + "\0" + key;
}

internal static class TestSchema
{
    public static ObjectType Orders { get; } = new(
        new Dictionary<string, SemanticType>(StringComparer.Ordinal)
        {
            ["id"] = SemanticTypeOptions.Int,
            ["total"] = SemanticTypeOptions.Decimal,
        });

    public static SourceRegistry Registry() =>
        new SourceRegistry()
            .Add(new TestSource("orders", Orders))
            .Freeze();

    public static QueryEngine Engine() => new();
}
