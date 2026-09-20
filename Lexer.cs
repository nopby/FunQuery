using FunQuery.Enums;
using System.Diagnostics;

namespace FunQuery;

class Lexer
{
    public static void Tokenize(TokenBuffer tokenBuffer, ReadOnlySpan<char> text)
    {
        var maxLength = tokenBuffer.Limits.MaxInputLength;

        if (text.Length > maxLength)
        {
            throw new QueryException(
                QueryErrorCode.InputTooLong,
                $"Expression is longer than {maxLength} characters.",
                maxLength,
                text.Length - maxLength);
        }

        int position = 0;
        while (position < text.Length)
        {
            char c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
                continue;
            }

            if (IsCallStart(c))
            {
                int startPosition = position++;
                while (position < text.Length &&
                       IsIdentifierPart(text[position]))
                {
                    position++;
                }

                tokenBuffer.Add(new Token(
                    TokenType.Call,
                    startPosition,
                    position));

                continue;
            }

            if (IsIdentifierStart(c))
            {
                int startPosition = position++;

                while (position < text.Length && IsIdentifierPart(text[position]))
                {
                    position++;
                }

                var value = text[startPosition..position];
                var type = value switch
                {
                    _ when IsComparisonOperator(value) == true => TokenType.ComparisonOperator,
                    _ when IsLogicalOperator(value) == true => TokenType.LogicalOperator,
                    _ => TokenType.Identifier,
                };

                tokenBuffer.Add(new Token(
                    type,
                    startPosition,
                    position));
                continue;
            }
            if (IsNumber(c))
            {
                int start = position++;

                while (position < text.Length && IsNumber(text[position]))
                    position++;

                // '.' dianggap bagian angka hanya bila diikuti digit,
                // supaya tidak bentrok dengan chain seperti $take(1).$filter(...)
                if (position + 1 < text.Length &&
                    text[position] == '.' &&
                    IsNumber(text[position + 1]))
                {
                    position++;

                    while (position < text.Length && IsNumber(text[position]))
                        position++;
                }

                // Menolak "2abc", "2m", "1.5f"
                if (position < text.Length && IsIdentifierStart(text[position]))
                    throw new QueryException(
                        QueryErrorCode.InvalidNumber,
                        "Invalid number.",
                        start, position - start + 1);

                tokenBuffer.Add(new Token(TokenType.Number, start, position));
                continue;
            }
            if (c == '\'')
            {
                int start = position++;

                while (position < text.Length &&
                       text[position] != '\'')
                {
                    position++;
                }

                if (position >= text.Length)
                    throw new QueryException(
                        QueryErrorCode.UnterminatedString,
                        "Unterminated string literal.",
                        start, text.Length - start);

                position++;

                tokenBuffer.Add(new Token(
                    TokenType.StringLiteral,
                    start,
                    position));

                continue;
            }
            switch (c)
            {
                case '(':
                    tokenBuffer.Add(new Token(
                        TokenType.OpenRoundParenthesis,
                        position,
                        ++position));
                    break;
                case ')':
                    tokenBuffer.Add(new Token(
                        TokenType.CloseRoundParenthesis,
                        position,
                        ++position));
                    break;
                case '.':
                    tokenBuffer.Add(new Token(
                        TokenType.Dot,
                        position,
                        ++position));
                    break;
                case '[':
                    tokenBuffer.Add(new Token(
                        TokenType.OpenSquareParenthesis,
                        position,
                        ++position));
                    break;
                case ']':
                    tokenBuffer.Add(new Token(
                        TokenType.CloseSquareParenthesis,
                        position,
                        ++position));
                    break;
                case '{':
                    tokenBuffer.Add(new Token(
                        TokenType.OpenCurlyParenthesis,
                        position,
                        ++position));
                    break;
                case '}':
                    tokenBuffer.Add(new Token(
                        TokenType.CloseCurlyParenthesis,
                        position,
                        ++position));
                    break;
                case ',':
                    tokenBuffer.Add(new Token(
                        TokenType.Comma,
                        position,
                        ++position));
                    break;
                case ':':
                    tokenBuffer.Add(new Token(
                        TokenType.Colon,
                        position,
                        ++position));
                    break;
                default:
                    throw new QueryException(
                        QueryErrorCode.UnexpectedCharacter,
                        $"Unexpected character '{c}'.",
                        position, 1);
            }
        }
    }
    private static bool IsIdentifierStart(char c) =>
        c is >= 'A' and <= 'Z'
          or >= 'a' and <= 'z'
          or '_';
    private static bool IsIdentifierPart(char c) =>
        IsIdentifierStart(c)
        || c is >= '0' and <= '9';
    private static bool IsNumber(char c) => c is >= '0' and <= '9';
    private static bool IsComparisonOperator(ReadOnlySpan<char> value) =>
        value.Equals("eq", StringComparison.Ordinal)
        || value.Equals("gt", StringComparison.Ordinal)
        || value.Equals("gte", StringComparison.Ordinal)
        || value.Equals("lt", StringComparison.Ordinal)
        || value.Equals("lte", StringComparison.Ordinal)
        || value.Equals("neq", StringComparison.Ordinal);
    private static bool IsLogicalOperator(ReadOnlySpan<char> value) =>
        value.Equals("or", StringComparison.Ordinal)
        || value.Equals("and", StringComparison.Ordinal);
    private static bool IsCallStart(char c) => c == '$';
}
