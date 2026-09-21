using FunQuery.Enums;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>
/// Every invalid input must report a stable error code and the exact place in the query
/// where it went wrong. One row per case, across the lexer, parser, and analyzer.
/// </summary>
public class ErrorPositionTests
{
    // ---- lexer
    [Theory]
    [InlineData("$source([{id:1}]).$filter(id eq 2abc)", QueryErrorCode.InvalidNumber, 32, 2)]
    [InlineData("$source([{id:1}]).$filter(id eq 'abc)", QueryErrorCode.UnterminatedString, 32, 5)]
    [InlineData("$source(#)", QueryErrorCode.UnexpectedCharacter, 8, 1)]
    // ---- parser: end of input and unexpected tokens
    [InlineData("$source([{id:1}]).$filter(id eq", QueryErrorCode.UnexpectedEndOfInput, 31, 0)]
    [InlineData("$source([{id:1}]).$filter(", QueryErrorCode.UnexpectedEndOfInput, 26, 0)]
    [InlineData("$source([{id:1}]).$filter(id eq 1) $x", QueryErrorCode.UnexpectedToken, 35, 2)]
    [InlineData("$source([{id:1}]).$filter(eq)", QueryErrorCode.UnexpectedToken, 26, 2)]
    // ---- parser: everything must live inside a function call
    [InlineData("id eq 2", QueryErrorCode.ExpressionOutsideFunction, 0, 7)]
    [InlineData("1", QueryErrorCode.ExpressionOutsideFunction, 0, 1)]
    [InlineData("$source([{id:1}]) eq 1", QueryErrorCode.ExpressionOutsideFunction, 0, 22)]
    [InlineData("'abc'.$filter(id eq 1)", QueryErrorCode.ExpressionOutsideFunction, 0, 5)]
    [InlineData("[{id:1}].$filter(id eq 1)", QueryErrorCode.ExpressionOutsideFunction, 0, 8)]
    // ---- parser: "name: value" only inside { }, and no trailing comma
    [InlineData("$source([{id:1}]).$filter(id: 2)", QueryErrorCode.UnexpectedToken, 28, 1)]
    [InlineData("$source([a: 1])", QueryErrorCode.UnexpectedToken, 10, 1)]
    [InlineData("{1, 2}", QueryErrorCode.UnexpectedToken, 1, 1)]
    [InlineData("$source([{id}])", QueryErrorCode.UnexpectedToken, 12, 1)]
    [InlineData("$source([{id:1,}])", QueryErrorCode.UnexpectedToken, 15, 1)]
    [InlineData("$source([{id:1 name:2}])", QueryErrorCode.UnexpectedToken, 15, 4)]
    [InlineData("$source([1,])", QueryErrorCode.UnexpectedToken, 11, 1)]
    [InlineData("$source([{id:1}],)", QueryErrorCode.UnexpectedToken, 17, 1)]
    // ---- parser: keywords are case-sensitive
    [InlineData("$source([{id:1}]).$filter(id EQ 1)", QueryErrorCode.UnexpectedToken, 29, 2)]
    [InlineData("$source([{id:1}]).$filter(id eq 1 AND id eq 2)", QueryErrorCode.UnexpectedToken, 34, 3)]
    // ---- analyzer: functions
    [InlineData("$nope(1)", QueryErrorCode.UnknownFunction, 0, 5)]
    [InlineData("$SOURCE([{id:1}])", QueryErrorCode.UnknownFunction, 0, 7)]
    [InlineData("$source([{id:1}]).$nope(1)", QueryErrorCode.UnknownFunction, 18, 5)]
    [InlineData("$filter(id eq 1)", QueryErrorCode.InvalidTarget, 0, 7)]
    [InlineData("$source([{id:1}]).$source([{id:1}])", QueryErrorCode.InvalidTarget, 18, 7)]
    [InlineData("$source([1,2]).$filter(id eq 2)", QueryErrorCode.InvalidTarget, 15, 7)]
    [InlineData("$source()", QueryErrorCode.InvalidArgumentCount, 0, 7)]
    [InlineData("$source([{id:1}],[{id:2}])", QueryErrorCode.InvalidArgumentCount, 0, 7)]
    [InlineData("$source([{id:1}]).$filter()", QueryErrorCode.InvalidArgumentCount, 18, 7)]
    [InlineData("$source([{id:1}]).$filter(id eq 1, id eq 2)", QueryErrorCode.InvalidArgumentCount, 18, 7)]
    // ---- analyzer: types
    [InlineData("$source(1)", QueryErrorCode.TypeMismatch, 8, 1)]
    [InlineData("$source([{id:1}]).$filter(id)", QueryErrorCode.TypeMismatch, 26, 2)]
    [InlineData("$source([{id:1}]).$filter(id eq 'a')", QueryErrorCode.TypeMismatch, 26, 9)]
    [InlineData("$source([{id:1}]).$filter(id eq 1 and id)", QueryErrorCode.TypeMismatch, 38, 2)]
    [InlineData("$source([{id:1}]).$filter(id and id eq 1)", QueryErrorCode.TypeMismatch, 26, 2)]
    [InlineData("$source([{id:1}]).$filter(id eq 1).$filter(id eq 'x')", QueryErrorCode.TypeMismatch, 43, 9)]
    [InlineData("$source([{id:1},{id:2,name:'x'}])", QueryErrorCode.IncompatibleElementTypes, 16, 15)]
    [InlineData("$source([1,'a'])", QueryErrorCode.IncompatibleElementTypes, 11, 3)]
    // ---- analyzer: names and numbers
    [InlineData("$source([{id:1}]).$filter(id gt null)", QueryErrorCode.TypeMismatch, 26, 10)]
    [InlineData("$source([{id:1}]).$filter(null)", QueryErrorCode.TypeMismatch, 26, 4)]
    [InlineData("$source([{id:1}]).$filter(zzz eq 1)", QueryErrorCode.UnknownIdentifier, 26, 3)]
    [InlineData("$source([{id:1}]).$filter(Id eq 1)", QueryErrorCode.UnknownIdentifier, 26, 2)]
    [InlineData("$source([{id:1,id:2}])", QueryErrorCode.DuplicateField, 15, 2)]
    [InlineData("$source([{id:1}]).$filter(id eq 99999999999999999999999999999999)", QueryErrorCode.NumberOutOfRange, 32, 32)]
    public void InvalidInput_ReportsCodePositionAndLength(
        string input,
        QueryErrorCode code,
        int position,
        int length)
    {
        QueryAssert.Fails(code, position, length, () => QueryPipeline.Analyze(input));
    }

    [Fact]
    public void EmptyInput_IsAnUnexpectedEndOfInputAtPositionZero()
    {
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedEndOfInput,
            0,
            0,
            () => QueryPipeline.Analyze(""));
    }

    [Fact]
    public void PositionIsNotRepeatedInTheMessage()
    {
        // ToDisplayString() already appends "(at position N)"; the message must not say it as well.
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidNumber,
            () => QueryPipeline.Analyze("$source([{id:1}]).$filter(id eq 2abc)"));

        Assert.False(error.Message.Contains("position", StringComparison.OrdinalIgnoreCase));
    }
}
