using System.Diagnostics;

namespace Console;

class Lexer
{
    public static void Tokenize(TokenBuffer tokenBuffer, ReadOnlySpan<char> text)
    {
        int position = 0;
        while (position < text.Length)
        {
            char c = text[position];
            if (char.IsWhiteSpace(c))
            {
                position++;
                continue;
            }

            if (IsFunctionStart(c))
            {
                int startPosition = position++;
                while (position < text.Length &&
                       IsIdentifierPart(text[position]))
                {
                    position++;
                }
                var value = text[startPosition..position];

                if (!IsFunction(value))
                {
                    throw new Exception(
                        $"Unknown function '{value.ToString()}'.");
                }

                tokenBuffer.Add(new Token(
                    TokenType.Function,
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
                int startValue = position++;

                while (position < text.Length &&
                       IsNumber(text[position]))
                {
                    position++;
                }

                TokenType type = TokenType.IntegerLiteral;

                if (position < text.Length &&
                    IsNumberSuffix(text[position]))
                {
                    char suffix = text[position++];

                    type = suffix switch
                    {
                        'l' or 'L' => TokenType.LongLiteral,
                        'd' or 'D' => TokenType.DoubleLiteral,
                        'm' or 'M' => TokenType.DecimalLiteral,
                        'f' or 'F' => TokenType.FloatLiteral,
                        _ => throw new UnreachableException()
                    };
                }

                tokenBuffer.Add(new Token(
                    type,
                    startValue,
                    position));

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
                    throw new Exception("Unterminated string literal.");

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
                    throw new Exception(
                        $"Unexpected character '{c}' at position {position}.");
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
    private static bool IsNumberSuffix(char c) =>
        c is 'm' || c is 'M'
        || c is 'd' || c is 'D'
        || c is 'f' || c is 'F'
        || c is 'l' || c is 'L';
    private static bool IsComparisonOperator(ReadOnlySpan<char> value) =>
        value.Equals("eq", StringComparison.OrdinalIgnoreCase)
        || value.Equals("gt", StringComparison.OrdinalIgnoreCase)
        || value.Equals("gte", StringComparison.OrdinalIgnoreCase)
        || value.Equals("lt", StringComparison.OrdinalIgnoreCase)
        || value.Equals("lte", StringComparison.OrdinalIgnoreCase)
        || value.Equals("neq", StringComparison.OrdinalIgnoreCase);
    private static bool IsLogicalOperator(ReadOnlySpan<char> value) =>
        value.Equals("or", StringComparison.OrdinalIgnoreCase)
        || value.Equals("and", StringComparison.OrdinalIgnoreCase);
    private static bool IsFunctionStart(char c) => c == '$';
    private static bool IsFunction(
        ReadOnlySpan<char> value) =>
        value.Equals("$filter", StringComparison.OrdinalIgnoreCase)
        || value.Equals("$select", StringComparison.OrdinalIgnoreCase)
        || value.Equals("$skip", StringComparison.OrdinalIgnoreCase)
        || value.Equals("$groupby", StringComparison.OrdinalIgnoreCase)
        || value.Equals("$order", StringComparison.OrdinalIgnoreCase)
        || value.Equals("$take", StringComparison.OrdinalIgnoreCase)
        || value.Equals("$source", StringComparison.OrdinalIgnoreCase);
}
