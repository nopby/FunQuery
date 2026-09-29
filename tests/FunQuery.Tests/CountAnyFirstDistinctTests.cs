using FunQuery.Enums;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>$count, $any, $first, and $distinct. Contract in docs/Functions.md.</summary>
public class CountAnyFirstDistinctTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // Shared: target and argument-count rules
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("$count")]
    [InlineData("$any")]
    [InlineData("$first")]
    [InlineData("$distinct")]
    public void EachFunctionRequiresAnArrayTarget(string fn)
    {
        QueryAssert.Fails(QueryErrorCode.InvalidTarget, () => QueryPipeline.Analyze($"{fn}()"));
    }

    [Fact]
    public void CountTakesNoArguments()
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidArgumentCount,
            () => QueryPipeline.Analyze("$source([{id:1}]).$count(true)"));
    }

    [Theory]
    [InlineData("$any")]
    [InlineData("$first")]
    [InlineData("$distinct")]
    public void EachFunctionAcceptsAtMostOneArgument(string fn)
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidArgumentCount,
            () => QueryPipeline.Analyze($"$source([{{id:1}}]).{fn}(id eq 1, id eq 2)"));
    }

    // ------------------------------------------------------------------
    // $count
    // ------------------------------------------------------------------

    [Fact]
    public void CountReturnsLong()
    {
        Assert.Equal("long", QueryPipeline.Analyze("$source([{id:1}]).$count()").SemanticType!.Name);
    }

    [Fact]
    public void CountOfAnEmptyArray_IsZero()
    {
        Assert.Equal("0", Run("$source([]).$count()"));
    }

    [Fact]
    public void CountMatchesTheNumberOfElements()
    {
        Assert.Equal("3", Run("$source([{id: 1}, {id: 2}, {id: 3}]).$count()"));
    }

    [Fact]
    public void CountAfterFilterCountsOnlyTheRemainingRows()
    {
        Assert.Equal(
            "2",
            Run("$source([{id: 1}, {id: 2}, {id: 3}]).$filter(id gt 1).$count()"));
    }

    [Fact]
    public void CountResultCannotBeChainedAsAnArray()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidTarget,
            () => QueryPipeline.Analyze("$source([{id:1}]).$count().$filter(true)"));

        Assert.Contains("long", error.Message);
    }

    // ------------------------------------------------------------------
    // $any
    // ------------------------------------------------------------------

    [Fact]
    public void AnyReturnsBool()
    {
        Assert.Equal("bool", QueryPipeline.Analyze("$source([{id:1}]).$any()").SemanticType!.Name);
    }

    [Fact]
    public void AnyWithoutAPredicate_IsTrueWhenThereIsAtLeastOneElement()
    {
        Assert.Equal("true", Run("$source([{id: 1}]).$any()"));
        Assert.Equal("false", Run("$source([]).$any()"));
    }

    [Fact]
    public void AnyWithAPredicate_TestsEachElement()
    {
        const string data = "[{id: 1}, {id: 2}]";

        Assert.Equal("true", Run($"$source({data}).$any(id eq 2)"));
        Assert.Equal("false", Run($"$source({data}).$any(id eq 5)"));
    }

    [Fact]
    public void AnyOnAnEmptyArrayWithAPredicate_IsFalse()
    {
        Assert.Equal("false", Run("$source([]).$any(true)"));
    }

    // ------------------------------------------------------------------
    // $first
    // ------------------------------------------------------------------

    [Fact]
    public void FirstReturnsTheElementType_NotAnArray()
    {
        var result = QueryPipeline.Analyze("$source([{id: 1}]).$first()");

        Assert.Equal("object", result.SemanticType!.Name);
    }

    [Fact]
    public void FirstWithoutAPredicate_ReturnsTheFirstElement()
    {
        Assert.Equal(
            """{"id":1}""",
            Run("$source([{id: 1}, {id: 2}]).$first()"));
    }

    [Fact]
    public void FirstOnAnEmptyArray_IsNull()
    {
        Assert.Equal("null", Run("$source([]).$first()"));
    }

    [Fact]
    public void FirstWithAPredicate_ReturnsTheFirstMatch()
    {
        Assert.Equal(
            """{"id":2}""",
            Run("$source([{id: 1}, {id: 2}, {id: 3}]).$first(id gt 1)"));
    }

    [Fact]
    public void FirstWithAPredicateThatMatchesNothing_IsNull()
    {
        Assert.Equal("null", Run("$source([{id: 1}, {id: 2}]).$first(id eq 5)"));
    }

    [Fact]
    public void FirstRespectsSortOrder()
    {
        Assert.Equal(
            """{"id":3}""",
            Run("$source([{id: 1}, {id: 3}, {id: 2}]).$sort(id, desc).$first()"));
    }

    [Fact]
    public void FirstOnAScalarArray_CanBeNullItself()
    {
        Assert.Equal("null", Run("$source([null, 1, 2]).$first()"));
    }

    // ------------------------------------------------------------------
    // $distinct: analysis
    // ------------------------------------------------------------------

    [Fact]
    public void DistinctReturnsTheSameArrayType()
    {
        Assert.Equal("array", QueryPipeline.Analyze("$source([{id:1}]).$distinct()").SemanticType!.Name);
    }

    [Fact]
    public void DistinctWithAKeyExpression_IsAnalyzedInElementScope()
    {
        Assert.NotNull(QueryPipeline.Analyze("$source([{g: 'a'}]).$distinct(g)").SemanticType);
    }

    // ------------------------------------------------------------------
    // $distinct: execution
    // ------------------------------------------------------------------

    [Fact]
    public void DistinctRemovesDuplicateScalars_KeepingTheFirstOccurrence()
    {
        Assert.Equal("[1,2,3]", Run("$source([1, 2, 1, 3, 2]).$distinct()"));
    }

    [Fact]
    public void DistinctOfAnEmptyArray_IsAnEmptyArray()
    {
        Assert.Equal("[]", Run("$source([]).$distinct()"));
    }

    [Fact]
    public void DistinctTreatsEqualNumbersOfDifferentTypesAsDuplicates()
    {
        Assert.Equal("[1,2]", Run("$source([1, 1.0, 2]).$distinct()"));
    }

    [Fact]
    public void DistinctTreatsMultipleNullsAsOneDuplicateGroup()
    {
        Assert.Equal("[null,1]", Run("$source([null, null, 1]).$distinct()"));
    }

    [Fact]
    public void DistinctWithAKey_GroupsByThatKeyOnly()
    {
        const string data = "[{g: 'a', n: 1}, {g: 'a', n: 2}, {g: 'b', n: 3}]";

        Assert.Equal(
            """[{"g":"a","n":1},{"g":"b","n":3}]""",
            Run($"$source({data}).$distinct(g)"));
    }

    [Fact]
    public void DistinctOnObjects_ComparesFieldsStructurally_IgnoringWriteOrder()
    {
        Assert.Equal(
            """[{"a":1,"b":2},{"a":1,"b":3}]""",
            Run("$source([{a: 1, b: 2}, {b: 2, a: 1}, {a: 1, b: 3}]).$distinct()"));
    }

    [Fact]
    public void DistinctOnObjects_TreatsAMissingFieldAsDifferentFromAnExplicitNull()
    {
        Assert.Equal(
            """[{"a":1},{"a":1,"b":null}]""",
            Run("$source([{a: 1}, {a: 1, b: null}]).$distinct()"));
    }

    [Fact]
    public void DistinctOnArraysOfArrays_ComparesElementsStructurally()
    {
        Assert.Equal(
            "[[1,2],[2,1]]",
            Run("$source([[1, 2], [1, 2], [2, 1]]).$distinct()"));
    }

    [Fact]
    public void DistinctCanBeChainedWithOtherFunctions()
    {
        Assert.Equal(
            "[1]",
            Run("$let(@x, 1).$source([1, 2, 1]).$distinct().$filter(~ eq @x)"));
    }
}
