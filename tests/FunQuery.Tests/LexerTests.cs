using FunQuery.Enums;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

public class LexerTests
{
    private static string TextOf(string input, Token token) =>
        input[token.StartPosition..token.EndPosition];

    // ------------------------------------------------------------------
    // Happy path
    // ------------------------------------------------------------------

    [Fact]
    public void FilterChain_ProducesExpectedTokenTypes()
    {
        var tokens = QueryPipeline.Tokenize("$source([1]).$filter(id eq 1)");

        Assert.Equal(
            new[]
            {
                TokenType.Call,
                TokenType.OpenRoundParenthesis,
                TokenType.OpenSquareParenthesis,
                TokenType.Number,
                TokenType.CloseSquareParenthesis,
                TokenType.CloseRoundParenthesis,
                TokenType.Dot,
                TokenType.Call,
                TokenType.OpenRoundParenthesis,
                TokenType.Identifier,
                TokenType.ComparisonOperator,
                TokenType.Number,
                TokenType.CloseRoundParenthesis,
            },
            tokens.Select(t => t.Type));
    }

    [Fact]
    public void ObjectLiteral_ProducesCurlyBracesColonAndComma()
    {
        var tokens = QueryPipeline.Tokenize("$source([{id: 1, name: 'a'}])");

        Assert.Contains(tokens, t => t.Type == TokenType.OpenCurlyParenthesis);
        Assert.Contains(tokens, t => t.Type == TokenType.CloseCurlyParenthesis);
        Assert.Single(tokens, t => t.Type == TokenType.Comma);
        Assert.Equal(2, tokens.Count(t => t.Type == TokenType.Colon));
    }

    [Fact]
    public void Whitespace_IsIgnored()
    {
        const string input = "  $source ( [ ] )  ";

        var tokens = QueryPipeline.Tokenize(input);

        Assert.Equal(5, tokens.Length);
        Assert.Equal("$source", TextOf(input, tokens[0]));
    }

    [Fact]
    public void EmptyInput_ProducesNoTokens()
    {
        Assert.Empty(QueryPipeline.Tokenize(""));
    }

    [Fact]
    public void StringLiteral_IncludesQuotesAndSpaces()
    {
        const string input = "$source(['hello world'])";

        var tokens = QueryPipeline.Tokenize(input);

        var literal = Assert.Single(tokens, t => t.Type == TokenType.StringLiteral);
        Assert.Equal("'hello world'", TextOf(input, literal));
    }

    [Fact]
    public void DecimalNumber_IsOneToken()
    {
        const string input = "$source([1.5])";

        var tokens = QueryPipeline.Tokenize(input);

        var number = Assert.Single(tokens, t => t.Type == TokenType.Number);
        Assert.Equal("1.5", TextOf(input, number));
    }

    [Fact]
    public void DotAfterInteger_IsNotPartOfTheNumber()
    {
        const string input = "1.$a";

        var tokens = QueryPipeline.Tokenize(input);

        Assert.Equal(
            new[] { TokenType.Number, TokenType.Dot, TokenType.Call },
            tokens.Select(t => t.Type));
        Assert.Equal("1", TextOf(input, tokens[0]));
    }

    // ------------------------------------------------------------------
    // Operator words vs identifiers
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("gt")]
    [InlineData("gte")]
    [InlineData("lt")]
    [InlineData("lte")]
    public void ComparisonWords_AreComparisonOperators(string word)
    {
        var token = Assert.Single(QueryPipeline.Tokenize(word));

        Assert.Equal(TokenType.ComparisonOperator, token.Type);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void LogicalWords_AreLogicalOperators(string word)
    {
        var token = Assert.Single(QueryPipeline.Tokenize(word));

        Assert.Equal(TokenType.LogicalOperator, token.Type);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("equals")]
    [InlineData("android")]
    [InlineData("order")]
    [InlineData("gt2")]
    [InlineData("_eq")]
    public void WordsThatOnlyStartLikeOperators_AreIdentifiers(string word)
    {
        var token = Assert.Single(QueryPipeline.Tokenize(word));

        Assert.Equal(TokenType.Identifier, token.Type);
    }

    // ------------------------------------------------------------------
    // Errors
    // ------------------------------------------------------------------

    [Fact]
    public void UnterminatedString_IsRejected()
    {
        QueryAssert.Fails(
            QueryErrorCode.UnterminatedString,
            () => QueryPipeline.Tokenize("$source(['abc])"));
    }

    [Fact]
    public void UnexpectedCharacter_IsRejected()
    {
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedCharacter,
            () => QueryPipeline.Tokenize("$source(#)"));
    }

    [Theory]
    [InlineData("$source([2abc])")]
    [InlineData("$source([1.5f])")]
    public void NumberFollowedByLetters_IsRejected(string input)
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidNumber,
            () => QueryPipeline.Tokenize(input));
    }

    // ------------------------------------------------------------------
    // Limits
    // ------------------------------------------------------------------

    [Fact]
    public void InputExactlyAtMaxLength_IsAccepted()
    {
        const string input = "$source([1])";
        var limits = new QueryLimits { MaxInputLength = input.Length };

        var tokens = QueryPipeline.Tokenize(input, limits);

        Assert.Equal(6, tokens.Length);
    }

    [Fact]
    public void InputLongerThanMaxLength_IsRejected()
    {
        const string input = "$source([1])";
        var limits = new QueryLimits { MaxInputLength = input.Length - 1 };

        QueryAssert.Fails(
            QueryErrorCode.InputTooLong,
            () => QueryPipeline.Tokenize(input, limits));
    }

    [Fact]
    public void MoreTokensThanMaxTokens_IsRejected()
    {
        // "$source([1])" has 6 tokens.
        var limits = new QueryLimits { MaxTokens = 3 };

        QueryAssert.Fails(
            QueryErrorCode.TooManyTokens,
            () => QueryPipeline.Tokenize("$source([1])", limits));
    }
}
