using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

/// <summary>not operand. Membungkus satu perbandingan atau kondisi: "not id eq 1" berarti not (id eq 1).</summary>
public sealed record NotExpression(BaseExpression Operand) : BaseExpression
{
    public override AstExpressionType Type => AstExpressionType.Not;

    public override SemanticType? SemanticType { get; set; }
}
