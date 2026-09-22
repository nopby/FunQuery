using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

/// <summary>'~': item saat ini pada step per-elemen, mis. $filter, $map (M4).</summary>
public sealed record TildeExpression(Token Token) : BaseExpression
{
    public override AstExpressionType Type => AstExpressionType.Tilde;

    public override SemanticType? SemanticType { get; set; }
}
