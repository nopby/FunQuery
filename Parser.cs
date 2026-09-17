using Console.Expressions;
using Console.Enums;

namespace Console;

public static class Parser
{
    public static BaseExpression? Parse(
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
    private static BaseExpression ParseExpression(
        ReadOnlySpan<char> source,
        ReadOnlySpan<Token> tokens,
        ref int position)
    {
        return ParseOr(
            source,
            tokens,
            ref position);
    }
    private static BaseExpression ParsePrimary(
        ReadOnlySpan<char> source, 
        ReadOnlySpan<Token> tokens, 
        ref int position)
    {
        if (position >= tokens.Length)
            throw new Exception("Expected expression, but reached end of input");

        if (tokens[position].Type == TokenType.OpenRoundParenthesis)
        {
            position++; 

            var inner = ParseExpression(source, tokens, ref position);

            if (position >= tokens.Length ||
                tokens[position].Type != TokenType.CloseRoundParenthesis)
            {
                throw new Exception("Expected ')' to close grouped expression.");
            }

            position++; 

            return inner;
        }

        return tokens[position].Type switch
        {
            TokenType.StringLiteral => ParseStringLiteral(tokens, ref position),
            TokenType.Identifier => ParseIdentifier(tokens, ref position),
            TokenType.Number => ParseNumber(tokens, ref position),
            _ => throw new Exception($"Expected expression, got {tokens[position].Type}.")
        };
    }
    private static BaseExpression ParseNumber(
        ReadOnlySpan<Token> tokens, 
        ref int position)
    {
        var token = Consume(
            tokens,
            ref position,
            TokenType.Number);

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
        var left = ParsePrimary(source, tokens, ref position);
        if (position >= tokens.Length ||
            tokens[position].Type != TokenType.ComparisonOperator)
        {
            return left;
        }
        var op = MatchComparisonOperator(source, tokens, ref position);
        var right = ParsePrimary(source, tokens, ref position);
        return new ComparisonExpression(left, op, right);
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

    private static BaseExpression ParseStringLiteral(
        ReadOnlySpan<Token> tokens,
        ref int position)
    {
        var token = Consume(
            tokens,
            ref position,
            TokenType.StringLiteral);

        return new ValueExpression(token);
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