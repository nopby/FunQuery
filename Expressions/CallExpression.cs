using Console.Enums;
using Console.SemanticTypes;

namespace Console.Expressions;

public sealed record CallExpression(
    BaseExpression? Target,
    Token Function,
    IReadOnlyList<BaseExpression> Arguments
) : BaseExpression
{
    public override AstExpressionType Type =>
        AstExpressionType.Call;

    public override SemanticType SemanticType { get; set; }
}