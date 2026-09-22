using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

/// <summary>Referensi ke variable, mis. @min. Token mencakup '@'.</summary>
public sealed record VariableExpression(Token Token) : BaseExpression
{
    public override AstExpressionType Type => AstExpressionType.Variable;

    public override SemanticType? SemanticType { get; set; }
}
