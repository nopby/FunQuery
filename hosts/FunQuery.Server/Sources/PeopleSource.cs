using FunQuery.AspNetCore;
using FunQuery.SemanticTypes;

namespace FunQuery.Server.Sources;

/// <summary>
/// Contoh source in-memory: orang. Satu baris sengaja tidak punya <c>nickname</c>, untuk
/// menunjukkan bahwa field yang hilang dibaca sebagai null.
/// </summary>
public sealed class PeopleSource : IQuerySource
{
    private static readonly ObjectType PersonSchema = new(
        new Dictionary<string, SemanticType>(StringComparer.Ordinal)
        {
            ["id"] = SemanticTypeOptions.Int,
            ["name"] = SemanticTypeOptions.String,
            ["nickname"] = SemanticTypeOptions.String,
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
            Row(1, "Ana", "ana", true),
            Row(2, "Budi", null, false),
            Row(3, "Citra", "ci", true),
        ];

        return ValueTask.FromResult(rows);
    }

    private static IReadOnlyDictionary<string, object?> Row(
        int id,
        string name,
        string? nickname,
        bool active) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["name"] = name,
            ["nickname"] = nickname,
            ["active"] = active,
        };
}
