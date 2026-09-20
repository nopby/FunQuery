using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

public sealed record CallExpression(
    BaseExpression? Target,
    Token Function,
    IReadOnlyList<BaseExpression> Arguments
) : BaseExpression
{
    public override AstExpressionType Type =>
        AstExpressionType.Call;

    public override SemanticType? SemanticType { get; set; }
}