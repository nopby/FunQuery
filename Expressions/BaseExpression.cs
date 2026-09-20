

using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

public abstract record BaseExpression
{
    public abstract AstExpressionType Type { get; }
    public abstract SemanticType SemanticType { get; set; }
}