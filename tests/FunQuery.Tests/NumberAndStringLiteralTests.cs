using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>Negative numbers, escaped quotes in strings, and quoted object keys.</summary>
public class NumberAndStringLiteralTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // Negative numbers: lexer
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("-10", 3)]
    [InlineData("-1.5", 4)]
    [InlineData("-0", 2)]
    [InlineData("-007", 4)]
    public void ANegativeNumber_IsOneToken_WithItsSign(string text, int end)
    {
        var token = Assert.Single(QueryPipeline.Tokenize(text));

        Assert.Equal(new Token(TokenType.Number, 0, end), token);
    }

    [Theory]
    [InlineData("- 1")]
    [InlineData("-a")]
    [InlineData("--1")]
    [InlineData("+1")]
    [InlineData("-")]
    public void AMinusThatDoesNotDirectlyPrecedeADigit_IsRejected(string text)
    {
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedCharacter,
            0,
            1,
            () => QueryPipeline.Tokenize(text));
    }

    [Fact]
    public void AMinusInTheMiddle_StartsANewNumber()
    {
        // There is no subtraction, so "1-2" is the two numbers 1 and -2.
        var tokens = QueryPipeline.Tokenize("1-2");

        Assert.Equal(
            new[] { new Token(TokenType.Number, 0, 1), new Token(TokenType.Number, 1, 3) },
            tokens);

        QueryAssert.Fails(QueryErrorCode.UnexpectedToken, () => QueryPipeline.Parse("$source([1-2])"));
    }

    [Fact]
    public void ANegativeNumberFollowedByLetters_IsAnInvalidNumber()
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidNumber,
            0,
            3,
            () => QueryPipeline.Tokenize("-2m"));
    }

    // ------------------------------------------------------------------
    // Negative numbers: types
    // ------------------------------------------------------------------

    private static string ElementTypeOf(string literal)
    {
        var result = QueryPipeline.Analyze($"$source([{literal}])");

        return Assert.IsType<ArrayType>(result.SemanticType).Type.Name;
    }

    [Theory]
    [InlineData("-1", "int")]
    [InlineData("-0", "int")]
    [InlineData("-2147483648", "int")]
    [InlineData("-2147483649", "long")]
    [InlineData("-9223372036854775808", "long")]
    [InlineData("-9223372036854775809", "decimal")]
    [InlineData("-1.5", "decimal")]
    [InlineData("-99999999999999999999", "decimal")]
    public void NegativeNumbers_GetTheNarrowestFittingType(string literal, string expected)
    {
        Assert.Equal(expected, ElementTypeOf(literal));
    }

    [Fact]
    public void ANegativeNumberBeyondDecimal_IsOutOfRange()
    {
        QueryAssert.Fails(
            QueryErrorCode.NumberOutOfRange,
            () => QueryPipeline.Analyze("$source([-" + new string('9', 40) + "])"));
    }

    // ------------------------------------------------------------------
    // Negative numbers: execution
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("n lt 0", """[{"n":-10},{"n":-5}]""")]
    [InlineData("n gte -5", """[{"n":-5},{"n":5}]""")]
    [InlineData("n eq -10", """[{"n":-10}]""")]
    [InlineData("n gt -10 and n lt 0", """[{"n":-5}]""")]
    [InlineData("-5 eq n", """[{"n":-5}]""")]
    public void NegativeIntegers_CompareByValue(string predicate, string expected)
    {
        Assert.Equal(expected, Run($"$source([{{n: -10}}, {{n: -5}}, {{n: 5}}]).$filter({predicate})"));
    }

    [Fact]
    public void NegativeDecimals_KeepTheirSignAndScale()
    {
        Assert.Equal(
            """[{"n":-1.50}]""",
            Run("$source([{n: -1.50}, {n: 2.5}]).$filter(n lt -1)"));
    }

    [Fact]
    public void NegativeLongs_AreComparedWithLongLiterals()
    {
        Assert.Equal(
            """[{"n":-5000000000}]""",
            Run("$source([{n: -5000000000}, {n: 5000000000}]).$filter(n lt -2147483649)"));
    }

    [Fact]
    public void MinusZeroEqualsZero()
    {
        Assert.Equal("""[{"n":0}]""", Run("$source([{n: -0}]).$filter(n eq 0)"));
    }

    // ------------------------------------------------------------------
    // Escaped quotes: lexer
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("'it''s'", 7)]
    [InlineData("''", 2)]
    [InlineData("''''", 4)]
    [InlineData("'a''''b'", 8)]
    [InlineData("'x'''", 5)]
    public void ADoubledQuote_IsPartOfTheString(string text, int end)
    {
        var token = Assert.Single(QueryPipeline.Tokenize(text));

        Assert.Equal(new Token(TokenType.StringLiteral, 0, end), token);
    }

    [Fact]
    public void TwoStringsSeparatedBySpace_AreTwoTokens()
    {
        var tokens = QueryPipeline.Tokenize("'a' 'b'");

        Assert.Equal(2, tokens.Length);
        Assert.All(tokens, t => Assert.Equal(TokenType.StringLiteral, t.Type));
    }

    [Theory]
    [InlineData("'abc", 0, 4)]
    [InlineData("'''", 0, 3)]
    [InlineData("x eq 'it''s", 5, 6)]
    public void AStringThatNeverCloses_IsUnterminated(string text, int position, int length)
    {
        QueryAssert.Fails(
            QueryErrorCode.UnterminatedString,
            position,
            length,
            () => QueryPipeline.Tokenize(text));
    }

    // ------------------------------------------------------------------
    // Escaped quotes: execution
    // ------------------------------------------------------------------

    [Fact]
    public void AnEscapedQuote_IsOneQuoteInTheValue()
    {
        Assert.Equal(
            """[{"t":"it's"}]""",
            Run("$source([{t: 'it''s'}, {t: 'x'}]).$filter(t eq 'it''s')"));
    }

    [Theory]
    [InlineData("''", "")]
    [InlineData("''''", "'")]
    [InlineData("'a''''b'", "a''b")]
    [InlineData("'''x'", "'x")]
    [InlineData("'x'''", "x'")]
    public void EveryDoubledQuoteBecomesOne(string literal, string expected)
    {
        var result = Engine.Execute($"$source([{literal}])");

        var list = Assert.IsType<List<object?>>(result.Value);

        Assert.Equal(expected, Assert.Single(list));
    }

    [Fact]
    public void AnEmptyStringIsNotNull()
    {
        Assert.Equal(
            """[{"t":""}]""",
            Run("$source([{t: ''}, {t: null}]).$filter(t eq '')"));
    }

    // ------------------------------------------------------------------
    // Quoted object keys
    // ------------------------------------------------------------------

    [Fact]
    public void AKeyCanBeAString_SoItMayContainAnyCharacter()
    {
        Assert.Equal(
            """[{"first-name":"Ana","in":1,"id":2}]""",
            Run("$source([{'first-name': 'Ana', 'in': 1, id: 2}]).$filter(id eq 2)"));
    }

    [Fact]
    public void AQuotedKey_IsTheSameFieldAsTheUnquotedName()
    {
        Assert.Equal(
            """[{"id":2}]""",
            Run("$source([{'id': 1}, {'id': 2}]).$filter(id eq 2)"));

        QueryAssert.Fails(
            QueryErrorCode.DuplicateField,
            18,
            1,
            () => Engine.Execute("$source([{'a': 1, a: 2}])"));
    }

    [Fact]
    public void ADoubledQuoteInAKey_IsUnescaped()
    {
        Assert.Equal("""[{"it's":1}]""", Run("$source([{'it''s': 1}])"));
    }

    [Fact]
    public void AKeyThatIsNeitherAnIdentifierNorAString_IsRejected()
    {
        QueryAssert.Fails(QueryErrorCode.UnexpectedToken, () => QueryPipeline.Parse("$source([{1: 2}])"));
    }
}
