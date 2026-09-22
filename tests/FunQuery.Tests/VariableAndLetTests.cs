using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>@variables and $let: declaration, scope, and $source(@var).</summary>
public class VariableAndLetTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // Lexer
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("@x", 0, 2)]
    [InlineData("@min", 0, 4)]
    [InlineData("@_a1", 0, 4)]
    public void AVariableReference_IsOneToken_IncludingTheAt(string text, int start, int end)
    {
        var token = Assert.Single(QueryPipeline.Tokenize(text));

        Assert.Equal(new Token(TokenType.Variable, start, end), token);
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@ x")]
    [InlineData("@1x")]
    public void AnAtNotFollowedByAnIdentifierCharacter_IsRejected(string text)
    {
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedCharacter,
            0,
            1,
            () => QueryPipeline.Tokenize(text));
    }

    [Fact]
    public void TwoVariablesSeparatedBySpace_AreTwoTokens()
    {
        var tokens = QueryPipeline.Tokenize("@a @b");

        Assert.Equal(2, tokens.Length);
        Assert.All(tokens, t => Assert.Equal(TokenType.Variable, t.Type));
    }

    // ------------------------------------------------------------------
    // Parser
    // ------------------------------------------------------------------

    [Fact]
    public void AVariable_ParsesAsAVariableExpression()
    {
        var call = Assert.IsType<CallExpression>(QueryPipeline.Parse("$filter(@x)"));
        var variable = Assert.IsType<VariableExpression>(Assert.Single(call.Arguments));

        Assert.Equal(new Token(TokenType.Variable, 8, 10), variable.Token);
        Assert.Equal(new SourceSpan(8, 10), variable.Span);
    }

    [Fact]
    public void AVariable_CanBeAnOperandOfAComparison()
    {
        var call = Assert.IsType<CallExpression>(QueryPipeline.Parse("$filter(id eq @x)"));
        var comparison = Assert.IsType<ComparisonExpression>(Assert.Single(call.Arguments));

        Assert.IsType<VariableExpression>(comparison.Right);
    }

    [Fact]
    public void ABareVariable_IsRejectedOutsideAFunction()
    {
        QueryAssert.Fails(QueryErrorCode.ExpressionOutsideFunction, () => QueryPipeline.Parse("@x"));
    }

    // ------------------------------------------------------------------
    // Analysis: undefined and redefined variables
    // ------------------------------------------------------------------

    [Fact]
    public void AnUndefinedVariable_IsRejectedWithItsPosition()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.UndefinedVariable,
            32,
            8,
            () => QueryPipeline.Analyze("$source([{id:1}]).$filter(id eq @missing)"));

        Assert.Contains("'@missing'", error.Message);
    }

    [Fact]
    public void RedefiningAVariable_IsRejectedWithTheSecondDeclarationsPosition()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.VariableRedefined,
            17,
            2,
            () => QueryPipeline.Analyze("$let(@x, 1).$let(@x, 2).$source([{n:1}])"));

        Assert.Contains("'@x'", error.Message);
    }

    [Theory]
    [InlineData("$let(1, 2).$source([{n:1}])", QueryErrorCode.InvalidLetTarget, 5, 1)]
    [InlineData("$let('x', 2).$source([{n:1}])", QueryErrorCode.InvalidLetTarget, 5, 3)]
    [InlineData("$let(id, 2).$source([{n:1}])", QueryErrorCode.InvalidLetTarget, 5, 2)]
    public void TheFirstArgumentOfLet_MustBeAVariable(
        string query,
        QueryErrorCode code,
        int position,
        int length)
    {
        QueryAssert.Fails(code, position, length, () => QueryPipeline.Analyze(query));
    }

    [Theory]
    [InlineData("$let(@x).$source([{n:1}])")]
    [InlineData("$let(@x, 1, 2).$source([{n:1}])")]
    [InlineData("$let().$source([{n:1}])")]
    public void LetRequiresExactlyTwoArguments(string query)
    {
        QueryAssert.Fails(QueryErrorCode.InvalidArgumentCount, () => QueryPipeline.Analyze(query));
    }

    [Fact]
    public void LetIsCaseSensitiveLikeEveryOtherFunction()
    {
        QueryAssert.Fails(
            QueryErrorCode.UnknownFunction,
            () => QueryPipeline.Analyze("$LET(@x, 1).$source([{n:1}])"));
    }

    // ------------------------------------------------------------------
    // $let is transparent to the data chain
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("$let(@x, 1).$source([{n:1}]).$filter(n eq @x)")]
    [InlineData("$source([{n:1}]).$let(@x, 1).$filter(n eq @x)")]
    [InlineData("$let(@a, 1).$let(@b, 2).$source([{n:1}]).$filter(n eq @a)")]
    public void LetCanBePlacedAnywhereInTheChain_WithoutBreakingTargetRules(string query)
    {
        Assert.NotNull(QueryPipeline.Analyze(query).SemanticType);
    }

    [Fact]
    public void SourceStillCannotFollowRealData_EvenThroughALet()
    {
        // $let is transparent, but it must not let $source hide behind it to dodge
        // TargetRule.Forbidden when real data already precedes it.
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidTarget,
            () => QueryPipeline.Analyze("$source([{n:1}]).$let(@x, 1).$source([{n:2}])"));

        Assert.Contains("must start the chain", error.Message);
    }

    [Fact]
    public void LetWithNoDataAnywhereInTheChain_StillFailsWhereDataIsRequired()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidTarget,
            () => QueryPipeline.Analyze("$let(@x, 1).$filter(true)"));

        Assert.Contains("$filter", error.Message);
    }

    [Fact]
    public void ALetAtTheVeryEndOfTheChain_IsAValidQuery()
    {
        // Unusual, but not an error: $let just passes its target through unchanged.
        var result = QueryPipeline.Analyze("$source([{n:1}]).$let(@x, 1)");

        Assert.IsType<ArrayType>(result.SemanticType);
    }

    // ------------------------------------------------------------------
    // Scoping: nested $let inside a value does not leak
    // ------------------------------------------------------------------

    [Fact]
    public void ALetNestedInsideAnotherLetsValue_DoesNotLeakOutward()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.UndefinedVariable,
            () => QueryPipeline.Analyze(
                "$let(@x, $let(@y, 1).$source([{n: @y}])).$source(@x).$filter(n eq @y)"));

        Assert.Contains("'@y'", error.Message);
    }

    [Fact]
    public void TheOuterLetsOwnBinding_IsStillVisibleAfterANestedLetInsideItsValue()
    {
        // @x is bound to an array of {n:int}, so "n eq @x" compares int with array: a type
        // mismatch, which proves @x itself WAS resolved (an undefined variable would report
        // UNDEFINED_VARIABLE instead).
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze(
                "$let(@x, $let(@y, 1).$source([{n: @y}])).$source(@x).$filter(n eq @x)"));

        Assert.Contains("Cannot compare", error.Message);
    }

    [Fact]
    public void RedefiningInsideANestedValue_DoesNotConflictWithTheOuterLevel()
    {
        // The inner @x (inside the value being bound to the outer @x) is a separate,
        // already-discarded scope, so this is not a redefinition.
        var result = QueryPipeline.Analyze(
            "$let(@x, $let(@x, 1).$source([{n: @x}])).$source([{m:1}]).$filter(m eq 1)");

        Assert.IsType<ArrayType>(result.SemanticType);
    }

    // ------------------------------------------------------------------
    // $source(@variable)
    // ------------------------------------------------------------------

    [Fact]
    public void SourceAcceptsAVariableBoundToAnArray()
    {
        var result = QueryPipeline.Analyze(
            "$let(@items, [{id: 1}, {id: 2}]).$source(@items).$filter(id eq 2)");

        Assert.IsType<ArrayType>(result.SemanticType);
    }

    [Theory]
    [InlineData("$let(@x, 1).$source(@x)", "int")]
    [InlineData("$let(@x, 'a').$source(@x)", "string")]
    [InlineData("$let(@x, true).$source(@x)", "bool")]
    [InlineData("$let(@x, {a: 1}).$source(@x)", "object")]
    public void SourceRejectsANonArrayVariable(string query, string typeName)
    {
        var error = QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => QueryPipeline.Analyze(query));

        Assert.Contains($"got {typeName}", error.Message);
    }

    [Fact]
    public void SourceOfAnUndefinedVariable_IsUndefinedVariable_NotTypeMismatch()
    {
        QueryAssert.Fails(QueryErrorCode.UndefinedVariable, () => QueryPipeline.Analyze("$source(@x)"));
    }

    // ------------------------------------------------------------------
    // Execution: values, order, and every literal kind
    // ------------------------------------------------------------------

    [Fact]
    public void ProgramSample_WorksEndToEnd()
    {
        Assert.Equal(
            """[{"age":18},{"age":25}]""",
            Run("$let(@min, 18)" +
                ".$source([{age: 17}, {age: 18}, {age: 25}])" +
                ".$filter(age gte @min)"));
    }

    [Fact]
    public void SourceOfAVariable_ReturnsExactlyThatArray()
    {
        Assert.Equal(
            """[{"id":1},{"id":2}]""",
            Run("$let(@items, [{id: 1}, {id: 2}]).$source(@items)"));
    }

    // $select does not exist yet, so a bound value cannot be read back directly. Instead,
    // each case proves the value round-tripped correctly by comparing it with the same
    // literal parsed independently: if @x really holds that value, "@x eq <literal>" is true.
    [Theory]
    [InlineData("1")]
    [InlineData("-1.5")]
    [InlineData("'a'")]
    [InlineData("''")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    public void AVariableCanBeBoundToAnyKindOfLiteral(string literal)
    {
        Assert.Equal(
            """[{"n":1}]""",
            Run($"$let(@x, {literal}).$source([{{n: 1}}]).$filter(@x eq {literal})"));
    }

    [Fact]
    public void AVariableCanBeBoundToAnArray()
    {
        // eq/neq reject arrays (docs/Operators.md), so this is verified through $source(@var)
        // instead, which is also its main intended use.
        Assert.Equal(
            """[{"id":1},{"id":2}]""",
            Run("$let(@x, [{id: 1}, {id: 2}]).$source(@x)"));
    }

    [Fact]
    public void AVariableCanBeBoundToAnObject_ThoughItCannotBeReadBackWithoutSelect()
    {
        // Binding an object is legal even though there is no $select yet to project it back out.
        // This is a smoke test that it analyzes and executes without error.
        var result = Engine.Execute("$let(@x, {n: 5}).$source([1])");

        Assert.Equal("array", result.Type.Name);
        Assert.Equal("[1]", result.ToJson());
    }

    [Fact]
    public void MultipleLetsBindIndependentVariables()
    {
        Assert.Equal(
            """[{"n":2}]""",
            Run("$let(@a, 1).$let(@b, 2)" +
                ".$source([{n:1}, {n:2}, {n:3}])" +
                ".$filter(n gt @a and n lt 3 or n eq @b)"));
    }

    [Fact]
    public void ALetsValueCanItselfBeAFullSubQuery()
    {
        Assert.Equal(
            """[{"n":1}]""",
            Run("$let(@x, $source([{n:1}, {n:2}]).$filter(n eq 1)).$source(@x)"));
    }

    [Fact]
    public void ALetsValueIsEvaluatedOnceRegardlessOfHowManyRowsFollow()
    {
        // If the value were re-evaluated per row, this would still give the same result,
        // so this test only proves correctness, not the "evaluated once" cost claim, which
        // is documented in docs/Variables.md instead.
        Assert.Equal(
            """[{"n":1},{"n":2},{"n":3}]""",
            Run("$let(@min, 1).$source([{n:1}, {n:2}, {n:3}]).$filter(n gte @min)"));
    }

    [Fact]
    public void NegativeAndDecimalValuesRoundTripThroughAVariable()
    {
        Assert.Equal(
            """[{"n":-1.50}]""",
            Run("$let(@x, -1.50).$source([{n: -1.50}, {n: 1.0}]).$filter(n eq @x)"));
    }

    [Fact]
    public void ANullBoundVariable_ComparesLikeAnyOtherNull()
    {
        Assert.Equal(
            """[{"n":null}]""",
            Run("$let(@x, null).$source([{n: 1}, {n: null}]).$filter(n eq @x)"));
    }
}
