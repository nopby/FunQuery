using Console.Enums;

namespace Console.Expressions;

public sealed record LogicalExpression(
    BaseExpression Left,
    LogicalOperator Operator,
    BaseExpression Right) : BaseExpression
{
    public override AstExpressionType Type => _type;
    private static readonly AstExpressionType _type = AstExpressionType.Logical;
}