using FunQuery.Enums;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>
/// $lower, $upper, $length, $coalesce, and $date. Contracts in docs/Functions.md.
/// </summary>
public class ScalarFunctionTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    private static string TypeOf(string query) =>
        QueryPipeline.Analyze(query).SemanticType!.Name;

    /// <summary>Menjalankan ekspresi di dalam predikat $any supaya bisa dipakai di top level.</summary>
    private static string Predicate(string expression) =>
        Run($"$source([1]).$any({expression})");

    // ------------------------------------------------------------------
    // Shared: leaf expressions and argument-count rules
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("$lower('a')")]
    [InlineData("$upper('a')")]
    [InlineData("$length('a')")]
    [InlineData("$coalesce(1)")]
    [InlineData("$date('2024-01-15')")]
    public void EachScalarFunctionCannotBeCalledOnATarget(string call)
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidTarget,
            () => QueryPipeline.Analyze($"$source([1]).{call}"));
    }

    [Theory]
    [InlineData("$lower")]
    [InlineData("$upper")]
    [InlineData("$length")]
    [InlineData("$date")]
    public void EachSingleArgumentFunctionRequiresExactlyOneArgument(string fn)
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidArgumentCount,
            () => QueryPipeline.Analyze($"{fn}()"));

        QueryAssert.Fails(
            QueryErrorCode.InvalidArgumentCount,
            () => QueryPipeline.Analyze($"{fn}('a', 'b')"));
    }

    [Fact]
    public void CoalesceRequiresAtLeastOneArgument()
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidArgumentCount,
            () => QueryPipeline.Analyze("$coalesce()"));
    }

    // ------------------------------------------------------------------
    // $lower and $upper
    // ------------------------------------------------------------------

    [Fact]
    public void LowerAndUpperAreInvariantAndDoNotChangeTheStaticType()
    {
        Assert.Equal("\"abc\"", Run("$lower('ABC')"));
        Assert.Equal("\"ABC\"", Run("$upper('abc')"));
        Assert.Equal("\"äöü\"", Run("$lower('ÄÖÜ')"));
        Assert.Equal("string", TypeOf("$lower('ABC')"));
        Assert.Equal("string", TypeOf("$upper('abc')"));
    }

    [Fact]
    public void LowerAndUpperPropagateNull()
    {
        Assert.Equal("null", Run("$lower(null)"));
        Assert.Equal("null", Run("$upper(null)"));
    }

    [Fact]
    public void LowerAndUpperWorkPerElement()
    {
        const string data = "[{name: 'Ana'}, {name: 'BOB'}]";

        Assert.Equal("[\"ana\",\"bob\"]", Run($"$source({data}).$map($lower(name))"));
        Assert.Equal("[\"ANA\",\"BOB\"]", Run($"$source({data}).$map($upper(name))"));
    }

    [Fact]
    public void LowerCanBeUsedInAPredicate()
    {
        const string data = "[{name: 'ANA'}, {name: 'bob'}]";

        Assert.Equal(
            "[{\"name\":\"ANA\"}]",
            Run($"$source({data}).$filter($lower(name) eq 'ana')"));
    }

    [Fact]
    public void LowerAndUpperAcceptOnlyStrings()
    {
        QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => QueryPipeline.Analyze("$lower(1)"));
        QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => QueryPipeline.Analyze("$upper(true)"));
        QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => QueryPipeline.Analyze("$lower([1])"));
    }

    [Fact]
    public void LowerRejectsANonStringValueThatWasOnlyKnownAsAny()
    {
        // $field(@col) is `any` at analysis time, so the runtime check is the one that fires.
        const string query =
            "$let(@col, 'a').$source([{a: 1}]).$map($lower($field(@col)))";

        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => Engine.Execute(query));

        Assert.Contains("at run time", error.Message);
    }

    [Fact]
    public void ScalarFunctionsAreNotSelectableAsListFormFields()
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidSelectArgument,
            () => QueryPipeline.Analyze("$source([{name: 'a'}]).$select($lower(name))"));
    }

    // ------------------------------------------------------------------
    // $length
    // ------------------------------------------------------------------

    [Fact]
    public void LengthReturnsLong()
    {
        Assert.Equal("long", TypeOf("$length('abc')"));
    }

    [Fact]
    public void LengthCountsUtf16CodeUnits()
    {
        Assert.Equal("0", Run("$length('')"));
        Assert.Equal("3", Run("$length('abc')"));
        Assert.Equal("2", Run("$length('😀')"));
    }

    [Fact]
    public void LengthPropagatesNull()
    {
        Assert.Equal("null", Run("$length(null)"));
        Assert.Equal("[null]", Run("$source([{name: null}]).$map($length(name))"));
    }

    [Fact]
    public void LengthCanBeUsedInAPredicateAndAsACount()
    {
        const string data = "[{name: 'a'}, {name: 'abcd'}]";

        Assert.Equal(
            "[{\"name\":\"abcd\"}]",
            Run($"$source({data}).$filter($length(name) gt 3)"));

        Assert.Equal(
            "[{\"id\":1},{\"id\":2}]",
            Run("$source([{id: 1}, {id: 2}, {id: 3}]).$take($length('ab'))"));
    }

    [Fact]
    public void LengthAcceptsOnlyStrings()
    {
        QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => QueryPipeline.Analyze("$length(1.5)"));
        QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze("$length($date('2024-01-15'))"));
    }

    // ------------------------------------------------------------------
    // $coalesce
    // ------------------------------------------------------------------

    [Fact]
    public void CoalesceReturnsTheFirstNonNullValue()
    {
        Assert.Equal("\"a\"", Run("$coalesce(null, 'a')"));
        Assert.Equal("\"a\"", Run("$coalesce('a', 'b')"));
        Assert.Equal("\"x\"", Run("$coalesce(null, null, 'x')"));
        Assert.Equal("null", Run("$coalesce(null, null)"));
        Assert.Equal("true", Run("$coalesce(null, true)"));
    }

    [Fact]
    public void CoalesceUsesTheUnifiedArgumentType()
    {
        Assert.Equal("int", TypeOf("$coalesce(null, 1)"));
        Assert.Equal("decimal", TypeOf("$coalesce(null, 1, 2.5)"));
        Assert.Equal("null", TypeOf("$coalesce(null, null)"));
        Assert.Equal("string", TypeOf("$coalesce(null, 'a', null)"));
        Assert.Equal("date", TypeOf("$coalesce(null, $date('2024-01-15'))"));
    }

    [Fact]
    public void CoalesceRejectsIncompatibleArgumentTypes()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            13,
            3,
            () => QueryPipeline.Analyze("$coalesce(1, 'a')"));

        Assert.Contains("compatible types", error.Message);

        QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze("$coalesce([1], ['a'])"));
    }

    [Fact]
    public void CoalesceWorksOnFieldsAndObjects()
    {
        const string data = "[{a: null, b: 'x'}, {a: 'y', b: 'z'}]";

        Assert.Equal(
            "[\"x\",\"y\"]",
            Run($"$source({data}).$map($coalesce(a, b))"));

        Assert.Equal("{\"a\":1}", Run("$coalesce(null, {a: 1})"));
        Assert.Equal("[1]", Run("$coalesce(null, [1], [2])"));
    }

    [Fact]
    public void CoalesceEvaluatesItsArgumentsLazily()
    {
        // The second argument would fail at run time (lower(2)), but it is never evaluated
        // because `a` is not null. The third case proves the argument is reachable at all.
        const string lazy =
            "$let(@col, 'b').$source([{a: 'x', b: 2}])" +
            ".$map($coalesce(a, $lower($field(@col))))";

        Assert.Equal("[\"x\"]", Run(lazy));

        const string eager =
            "$let(@col, 'b').$source([{a: null, b: 2}])" +
            ".$map($coalesce(a, $lower($field(@col))))";

        QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => Engine.Execute(eager));
    }

    [Fact]
    public void CoalesceCanBeUsedInASelectProjection()
    {
        const string data = "[{name: null}, {name: 'a'}]";

        Assert.Equal(
            "[{\"label\":\"?\"},{\"label\":\"a\"}]",
            Run($"$source({data}).$select({{label: $coalesce(name, '?')}})"));
    }

    // ------------------------------------------------------------------
    // $date
    // ------------------------------------------------------------------

    [Fact]
    public void DateProducesTheDateTypeAndIsoJson()
    {
        Assert.Equal("date", TypeOf("$date('2024-01-15')"));
        Assert.Equal("\"2024-01-15\"", Run("$date('2024-01-15')"));
        Assert.Equal("\"2024-02-29\"", Run("$date('2024-02-29')"));
    }

    [Fact]
    public void DatePropagatesNull()
    {
        Assert.Equal("null", Run("$date(null)"));
        Assert.Equal("[null]", Run("$source([{d: null}]).$map($date(d))"));
    }

    [Theory]
    [InlineData("'2024-1-5'")]
    [InlineData("'15/01/2024'")]
    [InlineData("'2024-13-01'")]
    [InlineData("'2024-00-10'")]
    [InlineData("'2023-02-29'")]
    [InlineData("'2024-01-15T00:00:00'")]
    [InlineData("' 2024-01-15'")]
    [InlineData("'2024-01-15 '")]
    [InlineData("''")]
    public void DateRejectsAnythingButStrictIsoFormat(string literal)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidDate,
            6,
            literal.Length,
            () => QueryPipeline.Analyze($"$date({literal})"));

        Assert.Contains("yyyy-MM-dd", error.Message);
    }

    [Fact]
    public void DateRejectsANonStringArgument()
    {
        QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => QueryPipeline.Analyze("$date(1)"));
        QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => QueryPipeline.Analyze("$date(true)"));
    }

    [Fact]
    public void DateParsesAFieldAndRejectsAnInvalidRuntimeValue()
    {
        Assert.Equal(
            "[\"2024-01-15\"]",
            Run("$source([{d: '2024-01-15'}]).$map($date(d))"));

        QueryAssert.Fails(
            QueryErrorCode.InvalidDate,
            () => Engine.Execute("$source([{d: 'nope'}]).$map($date(d))"));
    }

    [Fact]
    public void DateRejectsANonStringRuntimeValue()
    {
        const string query =
            "$let(@col, 'a').$source([{a: 20240115}]).$map($date($field(@col)))";

        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => Engine.Execute(query));

        Assert.Contains("at run time", error.Message);
    }

    [Fact]
    public void DatesCompareChronologically()
    {
        Assert.Equal("true", Predicate("$date('2024-01-15') eq $date('2024-01-15')"));
        Assert.Equal("false", Predicate("$date('2024-01-15') neq $date('2024-01-15')"));
        Assert.Equal("true", Predicate("$date('2024-06-01') gt $date('2024-01-15')"));
        Assert.Equal("true", Predicate("$date('2024-01-15') lte $date('2024-01-15')"));
        Assert.Equal("false", Predicate("$date('2024-01-15') eq null"));
        Assert.Equal("true", Predicate("$date(null) eq null"));
    }

    [Fact]
    public void DatesCannotBeComparedWithStrings()
    {
        QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze(
                "$source([1]).$any($date('2024-01-15') eq '2024-01-15')"));
    }

    [Fact]
    public void DatesWorkInPredicatesSortingAndDistinct()
    {
        const string data = "[{d: '2024-03-01'}, {d: '2024-01-01'}, {d: '2024-03-01'}]";

        Assert.Equal(
            "[{\"d\":\"2024-03-01\"},{\"d\":\"2024-03-01\"}]",
            Run($"$source({data}).$filter($date(d) gt $date('2024-02-01'))"));

        Assert.Equal(
            "[{\"d\":\"2024-01-01\"},{\"d\":\"2024-03-01\"},{\"d\":\"2024-03-01\"}]",
            Run($"$source({data}).$sort($date(d))"));

        Assert.Equal(
            "[{\"d\":\"2024-03-01\"},{\"d\":\"2024-01-01\"}]",
            Run($"$source({data}).$distinct($date(d))"));
    }

    [Fact]
    public void DatesWorkWithMembershipAndProjection()
    {
        const string data = "[{d: '2024-01-15'}, {d: '2024-02-01'}]";

        Assert.Equal(
            "[{\"d\":\"2024-02-01\"}]",
            Run($"$source({data}).$filter($date(d) in [$date('2024-02-01')])"));

        Assert.Equal(
            "[{\"day\":\"2024-01-15\"},{\"day\":\"2024-02-01\"}]",
            Run($"$source({data}).$select({{day: $date(d)}})"));
    }

    [Fact]
    public void DateCanBeHeldInAVariable()
    {
        Assert.Equal(
            "[\"2024-01-15\"]",
            Run("$let(@d, $date('2024-01-15')).$source([1]).$map(@d)"));
    }
}
