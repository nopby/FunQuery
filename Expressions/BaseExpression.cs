

using Console.Enums;

namespace Console.Expressions;

public abstract record BaseExpression
{
    public abstract AstExpressionType Type { get; }
}