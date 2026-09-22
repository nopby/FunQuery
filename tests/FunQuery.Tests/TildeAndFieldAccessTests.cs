using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>'~' (the current element) and dotted field paths (a.b, ~.a.b).</summary>
public class TildeAndFieldAccessTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // Lexer and parser
    // ------------------------------------------------------------------

    [Fact]
    public void Tilde_IsItsOwnToken()
    {
        var token = Assert.Single(QueryPipeline.Tokenize("~"));

        Assert.Equal(new Token(TokenType.Tilde, 0, 1), token);
    }

    [Fact]
    public void Tilde_ParsesAsATildeExpression()
    {
        var call = Assert.IsType<CallExpression>(QueryPipeline.Parse("$filter(~)"));

        Assert.IsType<TildeExpression>(Assert.Single(call.Arguments));
    }

    [Fact]
    public void ABareTilde_IsRejectedOutsideAFunction()
    {
        QueryAssert.Fails(QueryErrorCode.ExpressionOutsideFunction, () => QueryPipeline.Parse("~"));
    }

    [Fact]
    public void APathOnAnIdentifier_ChainsIntoNestedFieldAccessNodes()
    {
        const string query = "$filter(a.b.c eq 1)";

        var call = Assert.IsType<CallExpression>(QueryPipeline.Parse(query));
        var comparison = Assert.IsType<ComparisonExpression>(Assert.Single(call.Arguments));

        var c = Assert.IsType<FieldAccessExpression>(comparison.Left);
        var b = Assert.IsType<FieldAccessExpression>(c.Target);
        var a = Assert.IsType<IdentifierExpression>(b.Target);

        Assert.Equal("c", TokenText(query, c.Field));
        Assert.Equal("b", TokenText(query, b.Field));
        Assert.Equal("a", TokenText(query, a.Token));
    }

    private static string TokenText(string source, Token token) =>
        source[token.StartPosition..token.EndPosition];

    [Fact]
    public void APathCanStartFromTilde()
    {
        var call = Assert.IsType<CallExpression>(QueryPipeline.Parse("$filter(~.a.b eq 1)"));
        var comparison = Assert.IsType<ComparisonExpression>(Assert.Single(call.Arguments));

        var b = Assert.IsType<FieldAccessExpression>(comparison.Left);
        var a = Assert.IsType<FieldAccessExpression>(b.Target);

        Assert.IsType<TildeExpression>(a.Target);
    }

    [Fact]
    public void APathNode_SpansFromItsRootToItsLastField()
    {
        // "$filter(" is 8 characters, so "a.b.c" is 8..13.
        var call = Assert.IsType<CallExpression>(QueryPipeline.Parse("$filter(a.b.c eq 1)"));
        var comparison = Assert.IsType<ComparisonExpression>(Assert.Single(call.Arguments));

        Assert.Equal(new SourceSpan(8, 13), comparison.Left.Span);
    }

    [Fact]
    public void ADotFollowedByAFunctionCall_IsStillChaining_NotAPath()
    {
        // "a.$nope()" is not a valid path, since only identifiers (not calls) continue a path.
        // The dot is left for chain-parsing, which then rejects it because "a" is not a call.
        QueryAssert.Fails(
            QueryErrorCode.ExpressionOutsideFunction,
            () => QueryPipeline.Parse("$filter(a.$nope())"));
    }

    [Fact]
    public void ADotBetweenACallAndAFieldName_IsNotAPath()
    {
        // Chaining still requires a Call after '.', a path requires an Identifier;
        // "$source(...).x" is neither a valid chain nor a valid path.
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedToken,
            () => QueryPipeline.Parse("$source([1]).x"));
    }

    // ------------------------------------------------------------------
    // Analysis: '~' resolves to the element type, including scalars
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("$source([1, 2]).$filter(~ gt 1)")]
    [InlineData("$source(['a', 'b']).$filter(~ eq 'a')")]
    [InlineData("$source([true, false]).$filter(~)")]
    [InlineData("$source([{id: 1}]).$filter(~.id eq 1)")]
    public void Tilde_ResolvesToTheElementType(string query)
    {
        Assert.NotNull(QueryPipeline.Analyze(query).SemanticType);
    }

    [Fact]
    public void FilterWithNoTargetAtAll_FailsOnTheMissingTarget_NotOnTilde()
    {
        // $filter's own target check runs before its argument (~ eq 1) is ever analyzed,
        // so a missing target is reported as InvalidTarget, not ItemOutOfContext.
        QueryAssert.Fails(QueryErrorCode.InvalidTarget, () => QueryPipeline.Analyze("$filter(~ eq 1)"));
    }

    [Fact]
    public void Tilde_OutsideAnElementScope_IsItemOutOfContext()
    {
        // $let's value is analyzed in the surrounding scope, which here has no active
        // element scope (it sits between $source and the rest of the chain, not inside $filter).
        var error = QueryAssert.Fails(
            QueryErrorCode.ItemOutOfContext,
            25,
            1,
            () => QueryPipeline.Analyze("$source([1, 2]).$let(@x, ~).$filter(~ eq @x)"));

        Assert.Contains("'~'", error.Message);
    }

    [Fact]
    public void Tilde_AfterOnlyLetsWithNoRealArray_IsInvalidTarget()
    {
        // Here there genuinely is no element to refer to, so the target check (not ~ itself)
        // is what correctly fails.
        QueryAssert.Fails(
            QueryErrorCode.InvalidTarget,
            () => QueryPipeline.Analyze("$let(@x, 1).$filter(~ eq 1)"));
    }

    [Fact]
    public void TwoTildes_CanBeComparedForEqualityOnlyWhenScalar()
    {
        Assert.NotNull(QueryPipeline.Analyze("$source([1, 2]).$filter(~ eq ~)").SemanticType);

        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze("$source([{id: 1}]).$filter(~ eq ~)"));

        Assert.Contains("cannot be applied to object", error.Message);
    }

    // ------------------------------------------------------------------
    // Filter now accepts arrays of any element type, not just objects
    // ------------------------------------------------------------------

    [Fact]
    public void FilterOnAScalarArray_HasNoNamedFields()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.UnknownIdentifier,
            () => QueryPipeline.Analyze("$source([1, 2]).$filter(id eq 1)"));

        Assert.Contains("'id'", error.Message);
    }

    [Fact]
    public void FilterStillRejectsANonArrayTarget()
    {
        QueryAssert.Fails(QueryErrorCode.InvalidTarget, () => QueryPipeline.Analyze("$filter(true)"));
    }

    [Fact]
    public void FilterOnAnEmptyArray_NowSucceedsWhenThePredicateDoesNotNeedAnElementType()
    {
        // [] has element type "unknown"; a predicate that never touches ~ or a field is fine.
        Assert.Equal("array", QueryPipeline.Analyze("$source([]).$filter(true)").SemanticType!.Name);
    }

    [Fact]
    public void FilterOnAnEmptyArray_StillFailsWhenThePredicateNeedsTheElementType()
    {
        QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze("$source([]).$filter(~ eq 1)"));
    }

    // ------------------------------------------------------------------
    // Field access: types and errors
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("addr.city eq 'x'")]
    [InlineData("~.addr.city eq 'x'")]
    [InlineData("addr.geo.lat eq 1")]
    public void NestedObjectFields_CanBeAccessedThroughAPath(string predicate)
    {
        var query = "$source([{addr: {city: 'x', geo: {lat: 1, lng: 2}}}]).$filter(" + predicate + ")";

        Assert.NotNull(QueryPipeline.Analyze(query).SemanticType);
    }

    [Fact]
    public void AccessingAFieldThatDoesNotExistOnTheObject_IsUnknownIdentifier()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.UnknownIdentifier,
            () => QueryPipeline.Analyze("$source([{addr: {city: 'x'}}]).$filter(addr.zip eq 1)"));

        Assert.Contains("'zip'", error.Message);
    }

    [Theory]
    [InlineData("id.x eq 1")]
    [InlineData("~.id.x eq 1")]
    public void AccessingAFieldOnANonObjectValue_IsATypeMismatch(string predicate)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze("$source([{id: 1}]).$filter(" + predicate + ")"));

        Assert.Contains("Cannot access field 'x' on int", error.Message);
    }

    [Fact]
    public void APathThatReferencesAnUndefinedRootIdentifier_IsUnknownIdentifier_NotUnknownField()
    {
        // "addr" itself is not a field, so this fails before field access is even attempted.
        var error = QueryAssert.Fails(
            QueryErrorCode.UnknownIdentifier,
            () => QueryPipeline.Analyze("$source([{id: 1}]).$filter(addr.city eq 'x')"));

        Assert.Contains("Unknown identifier 'addr'", error.Message);
    }

    // ------------------------------------------------------------------
    // Execution
    // ------------------------------------------------------------------

    [Fact]
    public void TildeFiltersAScalarArrayByItsOwnValue()
    {
        Assert.Equal("[2,3]", Run("$source([1, 2, 3]).$filter(~ gt 1)"));
    }

    [Fact]
    public void NotTilde_NegatesTheScalarPredicate()
    {
        Assert.Equal("[1]", Run("$source([1, 2, 3]).$filter(not ~ gt 1)"));
    }

    [Fact]
    public void TildeWorksWithTextOperators()
    {
        Assert.Equal(
            """["bb"]""",
            Run("$source(['a', 'bb', 'ccc']).$filter(~ contains 'b' and not ~ contains 'c')"));
    }

    [Fact]
    public void TildeOnBooleans_CanBeUsedDirectlyAsThePredicate()
    {
        Assert.Equal("[true]", Run("$source([true, false]).$filter(~)"));
    }

    [Fact]
    public void TildeCanBeCompiledOnObjectRowsToo_ReturningTheWholeRow()
    {
        // ~ eq ~ is rejected by the analyzer for objects (arrays/objects aren't comparable),
        // but ~.field still works, proving ~ correctly refers to the row itself.
        Assert.Equal(
            """[{"id":1,"name":"a"}]""",
            Run("$source([{id: 1, name: 'a'}, {id: 2, name: 'b'}]).$filter(~.id eq 1)"));
    }

    [Fact]
    public void NestedFieldAccess_FiltersOnDeeplyNestedValues()
    {
        const string data =
            "[{id: 1, addr: {city: 'Jakarta', geo: {lat: 1}}}, " +
            "{id: 2, addr: {city: 'Bandung', geo: {lat: 2}}}]";

        Assert.Equal(
            """[{"id":1,"addr":{"city":"Jakarta","geo":{"lat":1}}}]""",
            Run($"$source({data}).$filter(addr.city eq 'Jakarta')"));

        Assert.Equal(
            """[{"id":2,"addr":{"city":"Bandung","geo":{"lat":2}}}]""",
            Run($"$source({data}).$filter(addr.geo.lat eq 2)"));
    }

    [Fact]
    public void FieldAccessAndTildeAccess_AgreeOnTheSameField()
    {
        const string data = "[{id: 1, addr: {city: 'Jakarta'}}, {id: 2, addr: {city: 'Bandung'}}]";

        Assert.Equal(
            Run($"$source({data}).$filter(addr.city eq 'Jakarta')"),
            Run($"$source({data}).$filter(~.addr.city eq 'Jakarta')"));
    }

    [Fact]
    public void FilteringAnEmptyArray_GivesAnEmptyArray()
    {
        Assert.Equal("[]", Run("$source([]).$filter(true)"));
    }

    [Fact]
    public void FieldPathCombinesWithVariablesAndNot()
    {
        Assert.Equal(
            """[{"id":2,"addr":{"city":"Bandung"}}]""",
            Run("$let(@city, 'Jakarta')" +
                ".$source([{id: 1, addr: {city: 'Jakarta'}}, {id: 2, addr: {city: 'Bandung'}}])" +
                ".$filter(not addr.city eq @city)"));
    }
}
