using FunQuery.Expressions;
using FunQuery.Enums;

namespace FunQuery;

public static class Parser
{
    public static BaseExpression Parse(
        ReadOnlySpan<char> source,
        ReadOnlySpan<Token> tokens,
        QueryLimits? limits = null)
    {
        int position = 0;
        var guard = new ParseGuard((limits ?? QueryLimits.Default).MaxDepth);

        var expression = ParseExpression(
            source,
            tokens,
            ref position, ref guard);

        if (position < tokens.Length)
        {
            var token = tokens[position];

            throw new QueryException(
                QueryErrorCode.UnexpectedToken,
                "Unexpected token.",
                token);
        }

        return expression;
    }
    private static int EndOfInput(ReadOnlySpan<Token> tokens) =>
    tokens.Length == 0 ? 0 : tokens[^1].EndPosition;
    private static BaseExpression ParseOr(
        ReadOnlySpan<char> source,
        ReadOnlySpan<Token> tokens,
        ref int position, ref ParseGuard guard)
    {
        var left = ParseAnd(source, tokens, ref position, ref guard);

        while (position < tokens.Length &&
               IsLogicalOperator(source, tokens[position], "or"))
        {
            MatchLogicalOperator(source, tokens, ref position);

            var right = ParseAnd(source, tokens, ref position, ref guard);

            left = ParseLogical(
                left,
                LogicalOperator.Or,
                right);
        }

        return left;
    }
    private static BaseExpression ParseAnd(
        ReadOnlySpan<char> source,
        ReadOnlySpan<Token> tokens,
        ref int position, ref ParseGuard guard)
    {
        var left = ParseComparison(
        source,
        tokens,
        ref position, ref guard);

        while (position < tokens.Length &&
               IsLogicalOperator(
                   source,
                   tokens[position],
                   "and"))
        {
            MatchLogicalOperator(
                source,
                tokens,
                ref position);

            var right = ParseComparison(
                source,
                tokens,
                ref position, ref guard);

            left = ParseLogical(
                left,
                LogicalOperator.And,
                right);
        }

        return left;
    }
    private static bool IsLogicalOperator(
        ReadOnlySpan<char> source,
        Token token,
        ReadOnlySpan<char> expected)
    {
        if (token.Type != TokenType.LogicalOperator)
            return false;

        return source[token.StartPosition..token.EndPosition]
            .Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
    private static BaseExpression ParsePostfix(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position, ref ParseGuard guard)
    {
        var expression = ParsePrimary(
            source,
            tokens,
            ref position, ref guard);

        while (Match(
            tokens,
            ref position,
            TokenType.Dot))
        {
            var function = Consume(
                tokens,
                ref position,
                TokenType.Call);

            var arguments = ParseFunctionArguments(
                source,
                tokens,
                ref position, ref guard);

            expression = new CallExpression(
                expression,
                function,
                arguments);
        }

        return expression;
    }
    private static BaseExpression ParseExpression(
        ReadOnlySpan<char> source,
        ReadOnlySpan<Token> tokens,
        ref int position, ref ParseGuard guard)
    {
        guard.Enter(
            position < tokens.Length
                ? tokens[position].StartPosition
                : (tokens.Length == 0 ? 0 : tokens[^1].EndPosition));

        var expression = ParseOr(source, tokens, ref position, ref guard);

        guard.Exit();

        return expression;
    }
    private static BaseExpression ParsePrimary(
        ReadOnlySpan<char> source, 
        ReadOnlySpan<Token> tokens, 
        ref int position, ref ParseGuard guard)
    {
        if (position >= tokens.Length)
            throw new QueryException(
                QueryErrorCode.UnexpectedEndOfInput,
                "Expected expression, but reached end of input.",
                EndOfInput(tokens), 0);

        return tokens[position].Type switch
        {
            TokenType.StringLiteral => ParseValue(tokens, TokenType.StringLiteral, ref position),
            TokenType.Identifier => ParseIdentifierOrNamed(
                source,
                tokens,
                ref position, ref guard),
            TokenType.Number => ParseValue(tokens, TokenType.Number, ref position),
            TokenType.OpenRoundParenthesis => ParseGroupedExpression(source, tokens, ref position, ref guard),
            TokenType.Call => ParseFunction(source, tokens, ref position, ref guard),
            TokenType.OpenSquareParenthesis => ParseArray(source, tokens, ref position, ref guard),
            TokenType.OpenCurlyParenthesis => ParseBlock(source, tokens, ref position, ref guard),
            _ => throw new QueryException(
                QueryErrorCode.UnexpectedToken,
                $"Expected expression, got {tokens[position].Type}.",
                tokens[position])
        };
    }
    private static BaseExpression ParseIdentifierOrNamed(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position, ref ParseGuard guard)
    {
        if (position + 1 < tokens.Length &&
            tokens[position + 1].Type == TokenType.Colon)
        {
            return ParseNamed(
                source,
                tokens,
                ref position, ref guard);
        }

        return ParseIdentifier(
            tokens,
            ref position);
    }
    private static NamedExpression ParseNamed(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position, ref ParseGuard guard)
    {
        var name = Consume(
        tokens,
        ref position,
        TokenType.Identifier);

        Consume(
            tokens,
            ref position,
            TokenType.Colon);

        var value = ParseExpression(
            source,
            tokens,
            ref position, ref guard);

        return new NamedExpression(name, value);
    }

    private static BlockExpression ParseBlock(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position, ref ParseGuard guard)
    {
        Consume(tokens, ref position, TokenType.OpenCurlyParenthesis);

        List<BaseExpression> expressions = [];

        while (position < tokens.Length &&
               tokens[position].Type != TokenType.CloseCurlyParenthesis)
        {
            expressions.Add(
                ParseExpression(source, tokens, ref position, ref guard));

            if (position < tokens.Length &&
                tokens[position].Type == TokenType.Comma)
            {
                position++;
                continue;
            }

            break;
        }

        Consume(
            tokens,
            ref position,
            TokenType.CloseCurlyParenthesis);

        return new BlockExpression(expressions);
    }
    private static BaseExpression ParseGroupedExpression(ReadOnlySpan<char> source, ReadOnlySpan<Token> tokens, ref int position, ref ParseGuard guard)
    {
        Consume(
        tokens,
        ref position,
        TokenType.OpenRoundParenthesis);

        var expression = ParseExpression(
        source,
        tokens,
        ref position, ref guard);

        Consume(
            tokens,
            ref position,
            TokenType.CloseRoundParenthesis);

        return expression;
    }
    private static BaseExpression ParseFunction(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position, ref ParseGuard guard)
    {
        var function = Consume(tokens, ref position, TokenType.Call);

        var arguments = ParseFunctionArguments(source, tokens, ref position, ref guard);

        return new CallExpression(null, function, arguments);
    }
    private static IReadOnlyList<BaseExpression> ParseFunctionArguments(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position, ref ParseGuard guard)
    {
        Consume(
            tokens,
            ref position,
            TokenType.OpenRoundParenthesis);

        List<BaseExpression> arguments = [];

        if (Match(
            tokens,
            ref position,
            TokenType.CloseRoundParenthesis))
        {
            return arguments;
        }

        while (true)
        {
            arguments.Add(
                ParseExpression(
                    source,
                    tokens,
                    ref position, ref guard));

            if (Match(
                tokens,
                ref position,
                TokenType.CloseRoundParenthesis))
            {
                break;
            }

            Consume(
                tokens,
                ref position,
                TokenType.Comma);
        }

        return arguments;
    }

    private static ArrayExpression ParseArray(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position, ref ParseGuard guard)
    {
        Consume(tokens, ref position, TokenType.OpenSquareParenthesis);

        List<BaseExpression> elements = [];

        if (Match(tokens, ref position, TokenType.CloseSquareParenthesis))
            return new ArrayExpression(elements);

        while (true)
        {
            elements.Add(
                ParseExpression(source, tokens, ref position, ref guard));

            if (Match(tokens, ref position, TokenType.CloseSquareParenthesis))
                break;

            Consume(tokens, ref position, TokenType.Comma);
        }

        return new ArrayExpression(elements);
    }

    private static BaseExpression ParseValue(
        ReadOnlySpan<Token> tokens, 
        TokenType tokenType,
        ref int position)
    {
        var token = Consume(
            tokens,
            ref position,
            tokenType);

        return new ValueExpression(token);
    }
    private static LogicalOperator MatchLogicalOperator(ReadOnlySpan<char> source,
        ReadOnlySpan<Token> tokens,
        ref int position)
    {
        var token = Consume(
            tokens,
            ref position,
            TokenType.LogicalOperator);

        var value = source[token.StartPosition..token.EndPosition];

        return value switch
        {
            _ when value.Equals("and", StringComparison.OrdinalIgnoreCase) => LogicalOperator.And,
            _ when value.Equals("or", StringComparison.OrdinalIgnoreCase) => LogicalOperator.Or,
            _ => throw new QueryException(
                QueryErrorCode.UnsupportedOperator,
                $"Operator '{value.ToString()}' is not implemented.",
                token)
        };
    }

    private static bool Match(
    ReadOnlySpan<Token> tokens,
    ref int position,
    TokenType type)
    {
        if (position >= tokens.Length ||
            tokens[position].Type != type)
        {
            return false;
        }

        position++;
        return true;
    }
    private static IdentifierExpression ParseIdentifier(
        ReadOnlySpan<Token> tokens,
        ref int position)
    {
        var token = Consume(
            tokens,
            ref position,
            TokenType.Identifier);

        return new IdentifierExpression(token);
    }
    private static BaseExpression ParseLogical(
        BaseExpression left,
        LogicalOperator op,
        BaseExpression right)
    {
        return new LogicalExpression(left, op, right);
    }
    private static BaseExpression ParseComparison(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position, ref ParseGuard guard)
    {
        var left = ParsePostfix(
            source,
            tokens,
            ref position, ref guard);

        if (position >= tokens.Length ||
            tokens[position].Type != TokenType.ComparisonOperator)
        {
            return left;
        }

        var op = MatchComparisonOperator(
            source,
            tokens,
            ref position);

        var right = ParsePostfix(
            source,
            tokens,
            ref position, ref guard);

        return new ComparisonExpression(
            left,
            op,
            right);
    }
    private static ComparisonOperator MatchComparisonOperator(
        ReadOnlySpan<char> source,
        ReadOnlySpan<Token> tokens,
        ref int position)
    {
        var token = Consume(
            tokens,
            ref position,
            TokenType.ComparisonOperator);

        var value = source[token.StartPosition..token.EndPosition];

        return value switch
        {
            _ when value.Equals("eq", StringComparison.OrdinalIgnoreCase) => ComparisonOperator.Equal,
            _ when value.Equals("neq", StringComparison.OrdinalIgnoreCase) => ComparisonOperator.NotEqual,
            _ when value.Equals("gt", StringComparison.OrdinalIgnoreCase) => ComparisonOperator.GreaterThan,
            _ when value.Equals("gte", StringComparison.OrdinalIgnoreCase) => ComparisonOperator.GreaterThanOrEqual,
            _ when value.Equals("lt", StringComparison.OrdinalIgnoreCase) => ComparisonOperator.LessThan,
            _ when value.Equals("lte", StringComparison.OrdinalIgnoreCase) => ComparisonOperator.LessThanOrEqual,
            _ => throw new QueryException(
                QueryErrorCode.UnsupportedOperator,
                $"Operator '{value.ToString()}' is not implemented.",
                token)
        };
    }
    private static Token Consume(
        ReadOnlySpan<Token> tokens,
        ref int position,
        TokenType expected)
    {
        if (position >= tokens.Length)
            throw new QueryException(
                QueryErrorCode.UnexpectedEndOfInput,
                $"Expected {expected}, but reached end of input.",
                EndOfInput(tokens), 0);

        var token = tokens[position];

        if (token.Type != expected)
        {
            throw new QueryException(
                QueryErrorCode.UnexpectedToken,
                $"Expected {expected}, got {token.Type}.",
                token);
        }

        position++;

        return token;
    }
}