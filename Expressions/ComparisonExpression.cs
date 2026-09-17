using Console.Enums;

namespace Console.Expressions;

public sealed record ComparisonExpression(
    BaseExpression Left,
    ComparisonOperator Operator,
    BaseExpression Right) : BaseExpression
{
    public override AstExpressionType Type => _type;
    private static readonly AstExpressionType _type = AstExpressionType.Comparison;
}