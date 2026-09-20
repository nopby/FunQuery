namespace FunQuery.SemanticTypes;

public sealed record ObjectType(
    IReadOnlyDictionary<string, SemanticType> Fields) : SemanticType
{
    public override string Name => "object";
}