using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Expressions;

/// <summary>
/// Path bertitik: a.b atau ~.a.b. Target adalah sisi kiri titik (identifier, ~, atau
/// FieldAccessExpression lain untuk path yang lebih dalam), Field adalah nama setelah titik.
/// </summary>
public sealed record FieldAccessExpression(BaseExpression Target, Token Field) : BaseExpression
{
    public override AstExpressionType Type => AstExpressionType.FieldAccess;

    public override SemanticType? SemanticType { get; set; }
}
