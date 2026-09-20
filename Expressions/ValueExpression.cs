using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

public sealed record ValueExpression(
    Token Token) : BaseExpression
{
    public override AstExpressionType Type => _type;

    public override SemanticType SemanticType { get; set; }

    private static readonly AstExpressionType _type = AstExpressionType.Value;
}