using Console.Enums;
using Console.SemanticTypes;

namespace Console.Expressions;

public sealed record NamedExpression(
    Token Name,
    BaseExpression Value
) : BaseExpression
{
    public override AstExpressionType Type => AstExpressionType.Named;

    public override SemanticType SemanticType { get; set; }
}