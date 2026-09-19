using Console.SemanticTypes;

namespace Console;

public sealed class ParameterDefinition
{
    public required string Name { get; init; }

    public required Type Type { get; init; }

    public bool Required { get; init; } = true;

    public bool Accepts(Type? type)
    {
        if (type is null)
            return false;

        return Type.IsAssignableFrom(type);
    }
}