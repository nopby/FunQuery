using FunQuery.AspNetCore;
using FunQuery.SemanticTypes;

namespace FunQuery.PostgreSql.Server.Sources;

/// <summary>Source in-memory sederhana untuk menunjukkan kedua jalur dalam satu host.</summary>
public sealed class PeopleSource : IQuerySource
{
    private static readonly ObjectType PersonSchema = new(
        new Dictionary<string, SemanticType>(StringComparer.Ordinal)
        {
            ["id"] = SemanticTypeOptions.Int,
            ["name"] = SemanticTypeOptions.String,
            ["active"] = SemanticTypeOptions.Boolean,
        });

    public string Name => "people";

    public ObjectType Schema => PersonSchema;

    public SourceCapabilities Capabilities => SourceCapabilities.All;

    public ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            Row(1, "Ana", true),
            Row(2, "Budi", false),
            Row(3, "Citra", true),
        ];

        return ValueTask.FromResult(rows);
    }

    private static IReadOnlyDictionary<string, object?> Row(
        int id,
        string name,
        bool active) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["name"] = name,
            ["active"] = active,
        };
}
