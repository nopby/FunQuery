using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>$index(). Contract in docs/Functions.md.</summary>
public class IndexTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // Analysis
    // ------------------------------------------------------------------

    [Fact]
    public void IndexReturnsInt()
    {
        Assert.Equal(
            "int",
            QueryPipeline.Analyze("$source([1]).$map($index())").SemanticType switch
            {
                ArrayType array => array.Type.Name,
                var other => other!.Name,
            });
    }

    [Fact]
    public void IndexOutsideAnyElementScopedFunction_IsItemOutOfContext()
    {
        QueryAssert.Fails(QueryErrorCode.ItemOutOfContext, () => QueryPipeline.Analyze("$index()"));
    }

    [Fact]
    public void IndexCannotBeCalledOnATarget()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidTarget,
            () => QueryPipeline.Analyze("$source([1]).$index()"));

        Assert.Contains("must start the chain", error.Message);
    }

    [Fact]
    public void IndexAcceptsAtMostOneArgument()
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidArgumentCount,
            () => QueryPipeline.Analyze("$source([1]).$map($index(1, 2))"));
    }

    [Theory]
    [InlineData("1.5", "decimal")]
    [InlineData("'a'", "string")]
    [InlineData("2147483648", "long")]
    public void IndexRejectsANonIntBase(string baseArgument, string typeName)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze($"$source([1]).$map($index({baseArgument}))"));

        Assert.Contains($"got {typeName}", error.Message);
    }

    [Fact]
    public void IndexAcceptsAnIntVariableAsBase()
    {
        Assert.NotNull(
            QueryPipeline.Analyze("$let(@n, 5).$source([1]).$map($index(@n))").SemanticType);
    }

    [Fact]
    public void IndexWithAnUndefinedVariableBase_IsUndefinedVariable()
    {
        QueryAssert.Fails(
            QueryErrorCode.UndefinedVariable,
            () => QueryPipeline.Analyze("$source([1]).$map($index(@missing))"));
    }

    [Theory]
    [InlineData("$filter")]
    [InlineData("$any")]
    [InlineData("$first")]
    [InlineData("$distinct")]
    public void IndexWorksInEveryElementScopedPredicateFunction(string fn)
    {
        Assert.NotNull(QueryPipeline.Analyze($"$source([1, 2]).{fn}($index() eq 0)").SemanticType);
    }

    [Fact]
    public void IndexWorksAsASortKey()
    {
        Assert.NotNull(QueryPipeline.Analyze("$source([1, 2]).$sort($index())").SemanticType);
    }

    // ------------------------------------------------------------------
    // Execution: basic counting
    // ------------------------------------------------------------------

    [Fact]
    public void IndexCountsFromZeroByDefault()
    {
        Assert.Equal("[0,1,2]", Run("$source(['a', 'b', 'c']).$map($index())"));
    }

    [Fact]
    public void IndexWithABase_StartsCountingFromThere()
    {
        Assert.Equal("[1,2,3]", Run("$source(['a', 'b', 'c']).$map($index(1))"));
        Assert.Equal("[10,11,12]", Run("$source(['a', 'b', 'c']).$map($index(10))"));
    }

    [Fact]
    public void IndexBaseCanComeFromAVariable()
    {
        Assert.Equal("[5,6]", Run("$let(@n, 5).$source(['a', 'b']).$map($index(@n))"));
    }

    [Fact]
    public void IndexInSelectObjectForm_GivesEveryEntryTheSameNumberPerRow()
    {
        Assert.Equal(
            """[{"no":1,"again":1,"id":1},{"no":2,"again":2,"id":2}]""",
            Run("$source([{id: 1}, {id: 2}]).$select({no: $index(1), again: $index(1), id: id})"));
    }

    // ------------------------------------------------------------------
    // Execution: each function's own input sequence
    // ------------------------------------------------------------------

    [Fact]
    public void FilterIndexReflectsPositionBeforeFiltering()
    {
        // Keeps elements at position 0 and 2 of the INPUT, by position, not by value.
        Assert.Equal(
            "[1,3]",
            Run("$source([1, 2, 3]).$filter($index() eq 0 or $index() eq 2)"));
    }

    [Fact]
    public void FilterThenSelectIndex_RestartsFromTheFilteredSequence()
    {
        Assert.Equal(
            """[{"no":0,"id":2},{"no":1,"id":3}]""",
            Run("$source([{id: 1}, {id: 2}, {id: 3}]).$filter(id gt 1)" +
                ".$select({no: $index(), id: id})"));
    }

    [Fact]
    public void SortThenMapIndex_ReflectsThePostSortOrder()
    {
        Assert.Equal(
            "[0,1,2]",
            Run("$source([{id: 3}, {id: 1}, {id: 2}]).$sort(id).$map($index())"));
    }

    [Fact]
    public void SortingByIndexItself_KeepsTheOriginalOrder()
    {
        // $index() inside $sort's key reports pre-sort position, so sorting by it is a no-op.
        Assert.Equal(
            Run("$source([{id: 3}, {id: 1}, {id: 2}])"),
            Run("$source([{id: 3}, {id: 1}, {id: 2}]).$sort($index())"));
    }

    [Fact]
    public void AnyAndFirstUseTheirOwnTargetsIndex()
    {
        Assert.Equal("true", Run("$source([{id: 1}, {id: 2}]).$any($index() eq 1)"));
        Assert.Equal(
            """{"id":2}""",
            Run("$source([{id: 1}, {id: 2}]).$first($index() eq 1)"));
    }

    [Fact]
    public void DistinctCanUseIndexAsAKey_MakingEveryElementUnique()
    {
        // Every element gets a distinct index, so nothing is removed.
        Assert.Equal(
            "[1,1,1]",
            Run("$source([1, 1, 1]).$distinct($index())"));
    }

    [Fact]
    public void NestedPerElementFunctions_EachGetTheirOwnIndependentIndex()
    {
        Assert.Equal(
            "[[0,1],[0]]",
            Run("$source([['x', 'y'], ['z']]).$map($source(~).$map($index()))"));
    }

    [Fact]
    public void IndexOfAnEmptyArray_IsAnEmptyArray()
    {
        Assert.Equal("[]", Run("$source([]).$map($index())"));
    }
}
