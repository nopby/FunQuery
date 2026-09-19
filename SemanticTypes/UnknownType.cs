namespace Console.SemanticTypes;


public sealed record UnknownType : SemanticType
{
    public override string Name => "unknown";
}