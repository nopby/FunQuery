using FunQuery.AspNetCore;
using FunQuery.SemanticTypes;

namespace FunQuery.AspNetCore.Tests.Support;

/// <summary>A source that never returns until the request is cancelled.</summary>
internal sealed class BlockingSource : IQuerySource
{
    public string Name => "blocked";

    public ObjectType Schema { get; } =
        TestSource.SchemaOf(("id", SemanticTypeOptions.Int));

    public SourceCapabilities Capabilities => SourceCapabilities.All;

    public async ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return [];
    }
}
