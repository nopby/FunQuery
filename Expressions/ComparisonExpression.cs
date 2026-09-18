using Console.Enums;
using Console.SemanticTypes;

namespace Console.Expressions;

public sealed record ComparisonExpression(
    BaseExpression Left,
    ComparisonOperator Operator,
    BaseExpression Right) : BaseExpression
{
    public override AstExpressionType Type => _type;

    public override SemanticType SemanticType { get; set; }

    private static readonly AstExpressionType _type = AstExpressionType.Comparison;
}