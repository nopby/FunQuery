using Console.Enums;

namespace Console.Expressions;

public sealed record IdentifierExpression(
    Token Token) : BaseExpression
{
    public override AstExpressionType Type => _type;
    private static readonly AstExpressionType _type = AstExpressionType.Identifier;
}