namespace FunQuery.SemanticTypes;

/// <summary>
/// Tipe dari literal <c>null</c>, dan dari field yang semua nilainya null.
/// Bisa dibandingkan dengan tipe skalar apa pun memakai eq dan neq.
/// </summary>
public sealed record NullType : SemanticType
{
    public override string Name => "null";
}
