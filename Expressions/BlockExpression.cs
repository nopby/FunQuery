using Console.Enums;
using Console.SemanticTypes;

namespace Console.Expressions;

public sealed record BlockExpression(IReadOnlyList<BaseExpression> Expressions) : BaseExpression
{
    public override AstExpressionType Type => AstExpressionType.Block;

    public override SemanticType SemanticType { get; set; }
}