using System.Globalization;
using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;

namespace FunQuery.Execution;

/// <summary>
/// Membaca nilai literal dari AST. Dipakai interpreter in-memory dan plan builder agar keduanya
/// tidak pernah berbeda pendapat tentang isi literal.
/// </summary>
public static class LiteralValues
{
    public static object? Parse(ValueExpression expression, ReadOnlyMemory<char> source)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var text = source.Span[
            expression.Token.StartPosition..expression.Token.EndPosition];

        return expression.Token.Type switch
        {
            TokenType.StringLiteral => Unquote(text),
            TokenType.BooleanLiteral => text.SequenceEqual("true"),
            TokenType.NullLiteral => null,
            TokenType.Number => ParseNumber(expression, text),
            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                $"Unsupported literal type: {expression.Token.Type}.",
                expression.Span),
        };
    }

    /// <summary>Membuang tanda kutip dan mengubah '' menjadi satu tanda kutip.</summary>
    public static string Unquote(ReadOnlySpan<char> tokenText) =>
        tokenText[1..^1].ToString().Replace("''", "'");

    private static object ParseNumber(
        ValueExpression expression,
        ReadOnlySpan<char> text)
    {
        var value = text.ToString();

        return expression.SemanticType switch
        {
            IntType => (object)int.Parse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture),

            LongType => (object)long.Parse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture),

            DecimalType => (object)decimal.Parse(
                value,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture),

            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                "Number literal has no numeric semantic type. Was the query analyzed?",
                expression.Span),
        };
    }
}
