using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

public sealed record NamedExpression(
    Token Name,
    BaseExpression Value
) : BaseExpression
{
    public override AstExpressionType Type => AstExpressionType.Named;

    public override SemanticType SemanticType { get; set; }
}