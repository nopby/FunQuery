using FunQuery.Enums;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>$sort, $take, and $skip. Contract in docs/Functions.md.</summary>
public class SortTakeSkipTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // $sort: analysis
    // ------------------------------------------------------------------

    [Fact]
    public void SortReturnsTheSameArrayType()
    {
        var result = QueryPipeline.Analyze("$source([{id: 1}]).$sort(id)");

        Assert.Equal("array", result.SemanticType!.Name);
    }

    [Fact]
    public void SortRequiresAnArrayTarget()
    {
        QueryAssert.Fails(QueryErrorCode.InvalidTarget, () => QueryPipeline.Analyze("$sort(id)"));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("[1]")]
    [InlineData("{a: 1}")]
    public void SortRejectsUnorderedKeyTypes(string key)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze($"$source([{{id: 1}}]).$sort({key})"));

        Assert.Contains("number or string key", error.Message);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    public void SortAcceptsNumberOrStringKeys(string key)
    {
        Assert.NotNull(QueryPipeline.Analyze($"$source([{{id: 1, name: 'a'}}]).$sort({key})").SemanticType);
    }

    [Theory]
    [InlineData("$source([{id:1}]).$sort()")]
    [InlineData("$source([{id:1}]).$sort(id, asc, desc)")]
    public void SortRequiresOneOrTwoArguments(string query)
    {
        QueryAssert.Fails(QueryErrorCode.InvalidArgumentCount, () => QueryPipeline.Analyze(query));
    }

    [Theory]
    [InlineData("up")]
    [InlineData("ascending")]
    public void SortRejectsAnUnrecognizedDirectionWord(string direction)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidSortDirection,
            () => QueryPipeline.Analyze($"$source([{{id:1}}]).$sort(id, {direction})"));

        Assert.Contains($"'{direction}'", error.Message);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("'desc'")]
    [InlineData("@x")]
    public void SortRejectsANonKeywordDirection(string direction)
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidSortDirection,
            () => QueryPipeline.Analyze($"$let(@x, 1).$source([{{id:1}}]).$sort(id, {direction})"));
    }

    [Fact]
    public void SortDirectionDoesNotResolveAsAField()
    {
        // "asc"/"desc" are only meaningful in this argument position; they are never looked up
        // as fields, so a row happening to have a field named "asc" is irrelevant here.
        Assert.NotNull(QueryPipeline.Analyze("$source([{id: 1, asc: 'x'}]).$sort(id, asc)").SemanticType);
    }

    // ------------------------------------------------------------------
    // $sort: execution
    // ------------------------------------------------------------------

    [Fact]
    public void SortOrdersAscendingByDefault()
    {
        Assert.Equal(
            """[{"id":1},{"id":2},{"id":3}]""",
            Run("$source([{id: 3}, {id: 1}, {id: 2}]).$sort(id)"));
    }

    [Fact]
    public void SortAscKeywordMatchesTheDefault()
    {
        Assert.Equal(
            Run("$source([{id: 3}, {id: 1}, {id: 2}]).$sort(id)"),
            Run("$source([{id: 3}, {id: 1}, {id: 2}]).$sort(id, asc)"));
    }

    [Fact]
    public void SortDescReversesTheOrder()
    {
        Assert.Equal(
            """[{"id":3},{"id":2},{"id":1}]""",
            Run("$source([{id: 3}, {id: 1}, {id: 2}]).$sort(id, desc)"));
    }

    [Fact]
    public void SortWorksOnScalarArraysViaTilde()
    {
        Assert.Equal("""["a","b","c"]""", Run("$source(['b', 'a', 'c']).$sort(~)"));
    }

    [Fact]
    public void SortIsStable_EqualKeysKeepTheirOriginalOrder()
    {
        const string data =
            "[{id: 1, g: 'a'}, {id: 2, g: 'a'}, {id: 3, g: 'b'}, {id: 4, g: 'b'}]";

        Assert.Equal(
            """[{"id":1,"g":"a"},{"id":2,"g":"a"},{"id":3,"g":"b"},{"id":4,"g":"b"}]""",
            Run($"$source({data}).$sort(g)"));
    }

    [Fact]
    public void SortIsStableInDescendingOrderToo()
    {
        // Within each group ("b" then "a"), original relative order (3 before 4, 1 before 2)
        // must be kept, not reversed.
        const string data =
            "[{id: 1, g: 'a'}, {id: 2, g: 'a'}, {id: 3, g: 'b'}, {id: 4, g: 'b'}]";

        Assert.Equal(
            """[{"id":3,"g":"b"},{"id":4,"g":"b"},{"id":1,"g":"a"},{"id":2,"g":"a"}]""",
            Run($"$source({data}).$sort(g, desc)"));
    }

    [Fact]
    public void NullKeysSortFirstAscendingAndLastDescending()
    {
        const string data = "[{id: 1, n: null}, {id: 2, n: 1}, {id: 3, n: null}, {id: 4, n: 2}]";

        Assert.Equal(
            """[{"id":1,"n":null},{"id":3,"n":null},{"id":2,"n":1},{"id":4,"n":2}]""",
            Run($"$source({data}).$sort(n)"));

        Assert.Equal(
            """[{"id":4,"n":2},{"id":2,"n":1},{"id":1,"n":null},{"id":3,"n":null}]""",
            Run($"$source({data}).$sort(n, desc)"));
    }

    [Fact]
    public void SortRemainsStableOnALargeNumberOfEqualKeys()
    {
        // A small sample can look "stable" by coincidence even without a tie-breaker, because
        // the sort algorithm never needs to reorder a handful of equal elements. A larger,
        // mostly-equal input is far more likely to expose a missing tie-breaker.
        var rows = string.Join(",", Enumerable.Range(0, 40).Select(i => $"{{id: {i}, g: {i % 2}}}"));
        var expectedIds = Enumerable.Range(0, 40).Where(i => i % 2 == 0)
            .Concat(Enumerable.Range(0, 40).Where(i => i % 2 == 1));
        var expected = "[" + string.Join(",", expectedIds.Select(i => $$"""{"id":{{i}},"g":{{i % 2}}}""")) + "]";

        Assert.Equal(expected, Run($"$source([{rows}]).$sort(g)"));
    }

    [Fact]
    public void SortByAFieldAccessPath()
    {
        const string data = "[{addr: {city: 'b'}}, {addr: {city: 'a'}}]";

        Assert.Equal(
            """[{"addr":{"city":"a"}},{"addr":{"city":"b"}}]""",
            Run($"$source({data}).$sort(addr.city)"));
    }

    [Fact]
    public void SortOfAnEmptyArray_StillNeedsAnOrderableKeyType()
    {
        // Same principle as $filter/$select on an empty array (docs/FieldAccess.md): [] has an
        // unknown element type, and $sort's key check has nothing to accept it against.
        QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => QueryPipeline.Analyze("$source([]).$sort(~)"));
    }

    [Fact]
    public void SortCanBeFollowedByOtherFunctions()
    {
        Assert.Equal(
            """[{"id":1}]""",
            Run("$source([{id: 3}, {id: 1}, {id: 2}]).$sort(id).$select(id).$filter(id eq 1)"));
    }

    // ------------------------------------------------------------------
    // $take / $skip: analysis
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("$take")]
    [InlineData("$skip")]
    public void TakeAndSkipRequireAnArrayTarget(string fn)
    {
        QueryAssert.Fails(QueryErrorCode.InvalidTarget, () => QueryPipeline.Analyze($"{fn}(1)"));
    }

    [Theory]
    [InlineData("$take", "'a'", "string")]
    [InlineData("$take", "1.5", "decimal")]
    [InlineData("$take", "true", "bool")]
    [InlineData("$skip", "'a'", "string")]
    public void TakeAndSkipRejectNonIntegerCounts(string fn, string count, string typeName)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze($"$source([{{id:1}}]).{fn}({count})"));

        Assert.Contains($"got {typeName}", error.Message);
    }

    [Theory]
    [InlineData("$take")]
    [InlineData("$skip")]
    public void TakeAndSkipAcceptALongCount(string fn)
    {
        Assert.NotNull(QueryPipeline.Analyze($"$source([{{id:1}}]).{fn}(2147483648)").SemanticType);
    }

    [Fact]
    public void TakeReturnsTheSameArrayType()
    {
        Assert.Equal("array", QueryPipeline.Analyze("$source([{id: 1}]).$take(1)").SemanticType!.Name);
    }

    [Theory]
    [InlineData("$take")]
    [InlineData("$skip")]
    public void TakeAndSkipCountCannotReferenceTheElement(string fn)
    {
        // The count is evaluated in the surrounding scope, not per element.
        QueryAssert.Fails(
            QueryErrorCode.ItemOutOfContext,
            () => QueryPipeline.Analyze($"$source([{{id:1}}]).{fn}(~)"));
    }

    // ------------------------------------------------------------------
    // $take / $skip: execution
    // ------------------------------------------------------------------

    private const string Three = "[{id: 1}, {id: 2}, {id: 3}]";

    [Fact]
    public void TakeKeepsTheFirstNElements()
    {
        Assert.Equal("""[{"id":1},{"id":2}]""", Run($"$source({Three}).$take(2)"));
    }

    [Fact]
    public void SkipDiscardsTheFirstNElements()
    {
        Assert.Equal("""[{"id":2},{"id":3}]""", Run($"$source({Three}).$skip(1)"));
    }

    [Theory]
    [InlineData(0, "[]")]
    [InlineData(-1, "[]")]
    [InlineData(-100, "[]")]
    [InlineData(100, """[{"id":1},{"id":2},{"id":3}]""")]
    public void TakeClampsItsCount(int count, string expected)
    {
        Assert.Equal(expected, Run($"$source({Three}).$take({count})"));
    }

    [Theory]
    [InlineData(0, """[{"id":1},{"id":2},{"id":3}]""")]
    [InlineData(-1, """[{"id":1},{"id":2},{"id":3}]""")]
    [InlineData(100, "[]")]
    public void SkipClampsItsCount(int count, string expected)
    {
        Assert.Equal(expected, Run($"$source({Three}).$skip({count})"));
    }

    [Fact]
    public void TakeAndSkipCanTakeACountFromAVariable()
    {
        Assert.Equal(
            """[{"id":1},{"id":2}]""",
            Run($"$let(@n, 2).$source({Three}).$take(@n)"));
    }

    [Fact]
    public void SortThenTakeGivesTheTopN()
    {
        Assert.Equal(
            """[{"id":3},{"id":2}]""",
            Run($"$source({Three}).$sort(id, desc).$take(2)"));
    }

    [Fact]
    public void TakeThenSkipPaginates()
    {
        Assert.Equal("""[{"id":2}]""", Run($"$source({Three}).$take(2).$skip(1)"));
    }

    [Fact]
    public void TakeOfAnEmptyArray_IsAnEmptyArray()
    {
        Assert.Equal("[]", Run("$source([]).$take(5)"));
    }
}
