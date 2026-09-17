using Console.Enums;

namespace Console.Expressions;

public sealed record ValueExpression(
    Token Token) : BaseExpression
{
    public override AstExpressionType Type => _type;
    private static readonly AstExpressionType _type = AstExpressionType.Value;
}