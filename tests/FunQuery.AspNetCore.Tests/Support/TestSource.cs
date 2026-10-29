using FunQuery.AspNetCore;
using FunQuery.SemanticTypes;

namespace FunQuery.AspNetCore.Tests.Support;

/// <summary>A controllable in-memory source for the adapter tests.</summary>
internal sealed class TestSource : IQuerySource
{
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _rows;

    public TestSource(
        string name,
        ObjectType schema,
        params IReadOnlyDictionary<string, object?>[] rows)
    {
        Name = name;
        Schema = schema;
        _rows = rows;
    }

    public string Name { get; }

    public ObjectType Schema { get; }

    public SourceCapabilities Capabilities => SourceCapabilities.All;

    /// <summary>Berapa kali <see cref="LoadAsync"/> dipanggil; membuktikan analisis mendahului load.</summary>
    public int LoadCount { get; private set; }

    public ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(
        CancellationToken cancellationToken)
    {
        LoadCount++;
        return ValueTask.FromResult(_rows);
    }

    public static IReadOnlyDictionary<string, object?> Row(
        params (string Name, object? Value)[] fields)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (name, value) in fields)
            row[name] = value;

        return row;
    }

    public static ObjectType SchemaOf(params (string Name, SemanticType Type)[] fields)
    {
        var map = new Dictionary<string, SemanticType>(StringComparer.Ordinal);

        foreach (var (name, type) in fields)
            map[name] = type;

        return new ObjectType(map);
    }
}
