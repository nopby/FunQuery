namespace Console.SemanticTypes;

public sealed record AnyType : SemanticType
{
    public override string Name => "any";
}