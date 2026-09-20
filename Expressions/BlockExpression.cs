using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

public sealed record BlockExpression(IReadOnlyList<BaseExpression> Expressions) : BaseExpression
{
    public override AstExpressionType Type => AstExpressionType.Block;

    public override SemanticType? SemanticType { get; set; }
}