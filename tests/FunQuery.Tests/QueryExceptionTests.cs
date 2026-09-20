using FunQuery.Enums;
using FunQuery.Extensions;

namespace FunQuery.Tests;

public class QueryExceptionTests
{
    // ------------------------------------------------------------------
    // Error code names (the public contract sent to clients)
    // ------------------------------------------------------------------

    [Fact]
    public void EveryErrorCode_HasItsOwnName()
    {
        var names = Enum.GetValues<QueryErrorCode>().Select(code => code.ToCode()).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void OnlyInternalError_MapsToInternalError()
    {
        // ToCode() falls back to "INTERNAL_ERROR". This fails when a new code is added
        // to the enum but forgotten in the switch.
        foreach (var code in Enum.GetValues<QueryErrorCode>())
        {
            if (code == QueryErrorCode.InternalError)
                Assert.Equal("INTERNAL_ERROR", code.ToCode());
            else
                Assert.NotEqual("INTERNAL_ERROR", code.ToCode());
        }
    }

    [Fact]
    public void ErrorCodeNames_AreUpperSnakeCase()
    {
        foreach (var code in Enum.GetValues<QueryErrorCode>())
        {
            var name = code.ToCode();

            Assert.NotEmpty(name);
            Assert.All(name, c => Assert.True(c is (>= 'A' and <= 'Z') or '_', $"{code} -> {name}"));
            Assert.False(name.StartsWith('_') || name.EndsWith('_') || name.Contains("__"), name);
        }
    }

    [Theory]
    [InlineData(QueryErrorCode.UnexpectedCharacter, "UNEXPECTED_CHARACTER")]
    [InlineData(QueryErrorCode.UnterminatedString, "UNTERMINATED_STRING")]
    [InlineData(QueryErrorCode.InvalidNumber, "INVALID_NUMBER")]
    [InlineData(QueryErrorCode.UnexpectedToken, "UNEXPECTED_TOKEN")]
    [InlineData(QueryErrorCode.UnexpectedEndOfInput, "UNEXPECTED_END_OF_INPUT")]
    [InlineData(QueryErrorCode.ExpressionOutsideFunction, "EXPRESSION_OUTSIDE_FUNCTION")]
    [InlineData(QueryErrorCode.UnknownFunction, "UNKNOWN_FUNCTION")]
    [InlineData(QueryErrorCode.UnknownIdentifier, "UNKNOWN_IDENTIFIER")]
    [InlineData(QueryErrorCode.TypeMismatch, "TYPE_MISMATCH")]
    [InlineData(QueryErrorCode.InvalidArgumentCount, "INVALID_ARGUMENT_COUNT")]
    [InlineData(QueryErrorCode.InvalidTarget, "INVALID_TARGET")]
    [InlineData(QueryErrorCode.IncompatibleElementTypes, "INCOMPATIBLE_ELEMENT_TYPES")]
    [InlineData(QueryErrorCode.DuplicateField, "DUPLICATE_FIELD")]
    [InlineData(QueryErrorCode.NumberOutOfRange, "NUMBER_OUT_OF_RANGE")]
    [InlineData(QueryErrorCode.InputTooLong, "INPUT_TOO_LONG")]
    [InlineData(QueryErrorCode.MaxDepthExceeded, "MAX_DEPTH_EXCEEDED")]
    [InlineData(QueryErrorCode.TooManyTokens, "TOO_MANY_TOKENS")]
    public void PublishedErrorCodeNames_DoNotChange(QueryErrorCode code, string expected)
    {
        // These strings are what API clients see. Renaming an enum member must not change them.
        Assert.Equal(expected, code.ToCode());
    }

    // ------------------------------------------------------------------
    // QueryException
    // ------------------------------------------------------------------

    [Fact]
    public void PositionAndLength_AreKeptWhenGiven()
    {
        var error = new QueryException(QueryErrorCode.UnexpectedToken, "x", 5, 3);

        Assert.Equal(5, error.Position);
        Assert.Equal(3, error.Length);
        Assert.True(error.HasPosition);
    }

    [Fact]
    public void MissingPosition_IsMinusOneWithZeroLength()
    {
        var withoutPosition = new QueryException(QueryErrorCode.UnexpectedToken, "x");
        var negative = new QueryException(QueryErrorCode.UnexpectedToken, "x", -7, 4);

        Assert.False(withoutPosition.HasPosition);
        Assert.Equal(-1, withoutPosition.Position);
        Assert.Equal(-1, negative.Position);
        Assert.Equal(0, negative.Length);
    }

    [Fact]
    public void NegativeLength_BecomesZero()
    {
        Assert.Equal(0, new QueryException(QueryErrorCode.UnexpectedToken, "x", 2, -5).Length);
    }

    [Fact]
    public void Position_CanComeFromATokenOrASpan()
    {
        var fromToken = new QueryException(
            QueryErrorCode.UnexpectedToken, "x", new Token(TokenType.Identifier, 4, 9));
        var fromSpan = new QueryException(
            QueryErrorCode.UnexpectedToken, "x", new SourceSpan(4, 9));

        Assert.Equal((4, 5), (fromToken.Position, fromToken.Length));
        Assert.Equal((4, 5), (fromSpan.Position, fromSpan.Length));
    }

    [Fact]
    public void DisplayString_ShowsCodeMessageAndPositionOnce()
    {
        var withPosition = new QueryException(QueryErrorCode.UnknownFunction, "Unknown function.", 3, 2);
        var withoutPosition = new QueryException(QueryErrorCode.UnknownFunction, "Unknown function.");

        Assert.Equal("[UNKNOWN_FUNCTION] Unknown function. (at position 3)", withPosition.ToDisplayString());
        Assert.Equal("[UNKNOWN_FUNCTION] Unknown function.", withoutPosition.ToDisplayString());
    }

    [Fact]
    public void OnlyInternalErrors_AreFlaggedAsInternal()
    {
        Assert.True(new QueryException(QueryErrorCode.InternalError, "bug").IsInternal);
        Assert.False(new QueryException(QueryErrorCode.UnknownFunction, "x").IsInternal);
    }

    [Fact]
    public void CodeName_MatchesToCode()
    {
        Assert.Equal("INVALID_TARGET", new QueryException(QueryErrorCode.InvalidTarget, "x").CodeName);
    }
}
