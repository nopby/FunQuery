

using Console.Enums;
using Console.SemanticTypes;

namespace Console.Expressions;

public abstract record BaseExpression
{
    public abstract AstExpressionType Type { get; }
    public abstract SemanticType SemanticType { get; set; }
}