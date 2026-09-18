using Console.Enums;
using Console.SemanticTypes;

namespace Console.Expressions;

public sealed record ValueExpression(
    Token Token) : BaseExpression
{
    public override AstExpressionType Type => _type;

    public override SemanticType SemanticType { get; set; }

    private static readonly AstExpressionType _type = AstExpressionType.Value;
}