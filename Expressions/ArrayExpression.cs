using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

public sealed record ArrayExpression(IReadOnlyList<BaseExpression> Elements) : BaseExpression
{
    public override AstExpressionType Type => AstExpressionType.Array;

    public override SemanticType? SemanticType { get; set; }
}