using Console.Enums;
using Console.SemanticTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Console.Expressions;

public sealed record FunctionExpression(Token Token, IReadOnlyList<BaseExpression> Arguments)
    : BaseExpression
{
    public override AstExpressionType Type => _type;

    public override SemanticType SemanticType { get; set; }

    private static readonly AstExpressionType _type = AstExpressionType.Function;
}