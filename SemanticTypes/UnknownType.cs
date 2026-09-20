namespace FunQuery.SemanticTypes;


public sealed record UnknownType : SemanticType
{
    public override string Name => "unknown";
}