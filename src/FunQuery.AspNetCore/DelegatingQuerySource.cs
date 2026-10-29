using FunQuery.SemanticTypes;

namespace FunQuery.AspNetCore;

/// <summary>
/// Membungkus <see cref="IQuerySourceExecutor"/> menjadi <see cref="IQuerySource"/> agar bisa
/// didaftarkan di <see cref="SourceRegistry"/> tanpa provider mengetahui tipe adapter HTTP.
/// </summary>
public sealed class DelegatingQuerySource : IQuerySource
{
    private readonly IQuerySourceExecutor _executor;

    public DelegatingQuerySource(
        string name,
        ObjectType schema,
        SourceCapabilities capabilities,
        IQuerySourceExecutor executor)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(executor);

        Name = name;
        Schema = schema;
        Capabilities = capabilities;
        _executor = executor;
    }

    public string Name { get; }

    public ObjectType Schema { get; }

    public SourceCapabilities Capabilities { get; }

    public IQuerySourceExecutor Executor => _executor;

    public ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This source is executed by its provider and is not loaded into memory.");
}

public static class QuerySourceExtensions
{
    /// <summary>Mendaftarkan source provider (mis. PostgreSQL) ke registry HTTP.</summary>
    public static IQuerySource AsQuerySource(
        this IQuerySourceExecutor executor,
        string name,
        ObjectType schema,
        SourceCapabilities capabilities = SourceCapabilities.All)
    {
        ArgumentNullException.ThrowIfNull(executor);

        return new DelegatingQuerySource(name, schema, capabilities, executor);
    }
}
