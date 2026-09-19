using Console.Expressions;
using Console.Enums;

namespace Console;

public static class Parser
{
    public static BaseExpression Parse(
        ReadOnlySpan<char> source,
        ReadOnlySpan<Token> tokens)
    {
        int position = 0;

        var expression = ParseExpression(
            source,
            tokens,
            ref position);

        if (position < tokens.Length)
        {
            var token = tokens[position];

            throw new Exception(
                $"Unexpected token at {token.StartPosition}.");
        }

        return expression;
    }
    private static BaseExpression ParseOr(
        ReadOnlySpan<char> source,
        ReadOnlySpan<Token> tokens,
        ref int position)
    {
        var left = ParseAnd(source, tokens, ref position);

        while (position < tokens.Length &&
               IsLogicalOperator(source, tokens[position], "or"))
        {
            MatchLogicalOperator(source, tokens, ref position);

            var right = ParseAnd(source, tokens, ref position);

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
        ref int position)
    {
        var left = ParseComparison(
        source,
        tokens,
        ref position);

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
                ref position);

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
    ref int position)
    {
        var expression = ParsePrimary(
            source,
            tokens,
            ref position);

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
                ref position);

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
        ref int position)
    {
        return ParseOr(source, tokens, ref position);
    }
    private static BaseExpression ParsePrimary(
        ReadOnlySpan<char> source, 
        ReadOnlySpan<Token> tokens, 
        ref int position)
    {
        if (position >= tokens.Length)
            throw new Exception("Expected expression, but reached end of input");

        return tokens[position].Type switch
        {
            TokenType.StringLiteral => ParseValue(tokens, TokenType.StringLiteral, ref position),
            TokenType.Identifier => ParseIdentifierOrNamed(
                source,
                tokens,
                ref position),
            TokenType.Number => ParseValue(tokens, TokenType.Number, ref position),
            TokenType.OpenRoundParenthesis => ParseGroupedExpression(source, tokens, ref position),
            TokenType.Call => ParseFunction(source, tokens, ref position),
            TokenType.OpenSquareParenthesis => ParseArray(source, tokens, ref position),
            TokenType.OpenCurlyParenthesis => ParseBlock(source, tokens, ref position),
            _ => throw new Exception($"Expected expression, got {tokens[position].Type}.")
        };
    }
    private static BaseExpression ParseIdentifierOrNamed(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position)
    {
        if (position + 1 < tokens.Length &&
            tokens[position + 1].Type == TokenType.Colon)
        {
            return ParseNamed(
                source,
                tokens,
                ref position);
        }

        return ParseIdentifier(
            tokens,
            ref position);
    }
    private static NamedExpression ParseNamed(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position)
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
            ref position);

        return new NamedExpression(name, value);
    }

    private static BlockExpression ParseBlock(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position)
    {
        Consume(tokens, ref position, TokenType.OpenCurlyParenthesis);

        List<BaseExpression> expressions = [];

        while (position < tokens.Length &&
               tokens[position].Type != TokenType.CloseCurlyParenthesis)
        {
            expressions.Add(
                ParseExpression(source, tokens, ref position));

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
    private static BaseExpression ParseGroupedExpression(ReadOnlySpan<char> source, ReadOnlySpan<Token> tokens, ref int position)
    {
        Consume(
        tokens,
        ref position,
        TokenType.OpenRoundParenthesis);

        var expression = ParseExpression(
        source,
        tokens,
        ref position);

        Consume(
            tokens,
            ref position,
            TokenType.CloseRoundParenthesis);

        return expression;
    }
    private static BaseExpression ParseFunction(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position)
    {
        var function = Consume(tokens, ref position, TokenType.Call);

        var arguments = ParseFunctionArguments(source, tokens, ref position);

        return new CallExpression(null, function, arguments);
    }
    private static IReadOnlyList<BaseExpression> ParseFunctionArguments(
    ReadOnlySpan<char> source,
    ReadOnlySpan<Token> tokens,
    ref int position)
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
                    ref position));

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
    ref int position)
    {
        Consume(tokens, ref position, TokenType.OpenSquareParenthesis);

        List<BaseExpression> elements = [];

        if (Match(tokens, ref position, TokenType.CloseSquareParenthesis))
            return new ArrayExpression(elements);

        while (true)
        {
            elements.Add(
                ParseExpression(source, tokens, ref position));

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
            _ => throw new NotImplementedException(
                $"Operator '{value.ToString()}' is not implemented.")
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
    ref int position)
    {
        var left = ParsePostfix(
            source,
            tokens,
            ref position);

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
            ref position);

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
            _ => throw new NotImplementedException(
                $"Operator '{value.ToString()}' is not implemented.")
        };
    }
    private static Token Consume(
        ReadOnlySpan<Token> tokens,
        ref int position,
        TokenType expected)
    {
        if (position >= tokens.Length)
            throw new Exception(
                $"Expected {expected}, but reached end of input.");

        var token = tokens[position];

        if (token.Type != expected)
        {
            throw new Exception(
                $"Expected {expected}, got {token.Type}.");
        }

        position++;

        return token;
    }
}