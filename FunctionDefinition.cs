using Console.SemanticTypes;

namespace Console;

public sealed class FunctionDefinition
{
    public required string Name { get; init; }

    public required IReadOnlyList<ParameterDefinition> Parameters { get; init; }

    public int RequiredArgumentCount { get; init; }

    public bool IsVariadic { get; init; }

    public Func<Type?, Type> ReturnType { get; init; } =
        _ => typeof(object);

    public Func<SemanticType?, bool> AcceptsTarget { get; init; } =
        _ => true;
}