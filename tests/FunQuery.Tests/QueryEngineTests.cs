using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>
/// End-to-end tests: query text in, JSON out. These cover the whole pipeline
/// (lexer, parser, analyzer, in-memory interpreter).
/// </summary>
public class QueryEngineTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    private const string Hello = "[{id: 1, name: 'hello'}, {id: 2, name: 'world'}]";

    // ------------------------------------------------------------------
    // Milestone 2 examples
    // ------------------------------------------------------------------

    [Fact]
    public void FilterById_ReturnsTheMatchingRow()
    {
        Assert.Equal(
            """[{"id":2,"name":"world"}]""",
            Run($"$source({Hello}).$filter(id eq 2)"));
    }

    [Fact]
    public void FilterWithGte_ReturnsEveryRow()
    {
        Assert.Equal(
            """[{"id":1,"name":"hello"},{"id":2,"name":"world"}]""",
            Run($"$source({Hello}).$filter(id gte 1)"));
    }

    [Fact]
    public void Source_ReturnsTheInlineArrayUnchanged()
    {
        Assert.Equal(
            """[{"id":1,"name":"hello"},{"id":2,"name":"world"}]""",
            Run($"$source({Hello})"));
    }

    [Fact]
    public void Source_OfAnEmptyArray_IsAnEmptySequence()
    {
        Assert.Equal("[]", Run("$source([])"));
    }

    [Fact]
    public void WhitespaceAndLineBreaksBetweenTokens_AreIgnored()
    {
        var query = """
            $source([
                {id: 1, name: 'hello'},
                {id: 2, name: 'world'}
            ])
            .$filter(
                id eq 2
            )
            """;

        Assert.Equal("""[{"id":2,"name":"world"}]""", Run(query));
    }

    // ------------------------------------------------------------------
    // Empty results
    // ------------------------------------------------------------------

    [Fact]
    public void FilterWithoutMatches_IsAnEmptySequenceNotNull()
    {
        var result = Engine.Execute($"$source({Hello}).$filter(id eq 99)");

        var list = Assert.IsType<List<object?>>(result.Value);
        Assert.Empty(list);
        Assert.Equal("[]", result.ToJson());
        Assert.Equal("array", result.Type.Name);
    }

    [Fact]
    public void FilterThatExcludesEverything_KeepsTheArrayType()
    {
        Assert.Equal("[]", Run($"$source({Hello}).$filter(false)"));
    }

    // ------------------------------------------------------------------
    // Comparison operators on numbers
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("id eq 2", """[{"id":2}]""")]
    [InlineData("id neq 2", """[{"id":1},{"id":3}]""")]
    [InlineData("id gt 1", """[{"id":2},{"id":3}]""")]
    [InlineData("id gte 2", """[{"id":2},{"id":3}]""")]
    [InlineData("id lt 3", """[{"id":1},{"id":2}]""")]
    [InlineData("id lte 2", """[{"id":1},{"id":2}]""")]
    [InlineData("2 eq id", """[{"id":2}]""")]
    [InlineData("2 gt id", """[{"id":1}]""")]
    [InlineData("id eq id", """[{"id":1},{"id":2},{"id":3}]""")]
    public void EveryComparisonOperator_Works(string predicate, string expected)
    {
        Assert.Equal(expected, Run($"$source([{{id: 1}}, {{id: 2}}, {{id: 3}}]).$filter({predicate})"));
    }

    [Fact]
    public void ResultKeepsTheOrderOfTheSource()
    {
        Assert.Equal(
            """[{"id":3},{"id":1},{"id":2}]""",
            Run("$source([{id: 3}, {id: 1}, {id: 2}]).$filter(id gt 0)"));
    }

    [Fact]
    public void IntAndDecimal_AreComparedByValue()
    {
        Assert.Equal("""[{"n":1}]""", Run("$source([{n: 1}, {n: 2}]).$filter(n eq 1.0)"));
        Assert.Equal("""[{"n":2}]""", Run("$source([{n: 1}, {n: 2}]).$filter(n gt 1.5)"));
    }

    [Fact]
    public void LongValues_AreComparedWithIntLiterals()
    {
        Assert.Equal(
            """[{"n":5000000000}]""",
            Run("$source([{n: 2147483648}, {n: 5000000000}]).$filter(n gt 2147483648)"));

        Assert.Equal(
            """[{"n":2147483648}]""",
            Run("$source([{n: 2147483648}, {n: 5000000000}]).$filter(n eq 2147483648.0)"));
    }

    [Fact]
    public void DecimalScale_DoesNotAffectEquality_ButIsKeptInTheOutput()
    {
        Assert.Equal(
            """[{"n":1.50}]""",
            Run("$source([{n: 1.50}, {n: 2.5}]).$filter(n eq 1.5)"));
    }

    // ------------------------------------------------------------------
    // Strings
    // ------------------------------------------------------------------

    private const string Names = "[{name: 'apple'}, {name: 'Banana'}, {name: 'cherry'}]";

    [Fact]
    public void StringEquality_IsCaseSensitive()
    {
        Assert.Equal("[]", Run($"$source({Names}).$filter(name eq 'Apple')"));
        Assert.Equal("""[{"name":"apple"}]""", Run($"$source({Names}).$filter(name eq 'apple')"));
    }

    [Fact]
    public void StringOrdering_IsOrdinal()
    {
        // 'B' (66) sorts before 'a' (97) in ordinal order.
        Assert.Equal("""[{"name":"Banana"}]""", Run($"$source({Names}).$filter(name lt 'a')"));
        Assert.Equal("""[{"name":"cherry"}]""", Run($"$source({Names}).$filter(name gte 'b')"));
    }

    [Fact]
    public void Strings_CanContainSpacesAndUnicode()
    {
        Assert.Equal(
            """[{"t":"héllo wörld ✓"}]""",
            Run("$source([{t: 'héllo wörld ✓'}]).$filter(t eq 'héllo wörld ✓')"));
    }

    // ------------------------------------------------------------------
    // Booleans
    // ------------------------------------------------------------------

    private const string Flags = "[{id: 1, on: true}, {id: 2, on: false}]";

    [Fact]
    public void BooleanLiterals_CanBeComparedWithEqAndNeq()
    {
        Assert.Equal("""[{"id":1,"on":true}]""", Run($"$source({Flags}).$filter(on eq true)"));
        Assert.Equal("""[{"id":2,"on":false}]""", Run($"$source({Flags}).$filter(on neq true)"));
        Assert.Equal("""[{"id":2,"on":false}]""", Run($"$source({Flags}).$filter(on eq false)"));
    }

    [Fact]
    public void ABooleanFieldCanBeThePredicate()
    {
        Assert.Equal("""[{"id":1,"on":true}]""", Run($"$source({Flags}).$filter(on)"));
        Assert.Equal("""[{"id":1,"on":true}]""", Run($"$source({Flags}).$filter(on and id eq 1)"));
        Assert.Equal("[]", Run($"$source({Flags}).$filter(on and id eq 2)"));
    }

    [Fact]
    public void ABooleanLiteralCanBeThePredicate()
    {
        Assert.Equal(
            """[{"id":1,"on":true},{"id":2,"on":false}]""",
            Run($"$source({Flags}).$filter(true)"));

        Assert.Equal("[]", Run($"$source({Flags}).$filter(false)"));
    }

    // ------------------------------------------------------------------
    // null: two-valued semantics (docs/Operators.md)
    // ------------------------------------------------------------------

    private const string WithNull = "[{id: 1, name: 'a'}, {id: 2, name: null}, {id: 3, name: 'c'}]";

    [Theory]
    [InlineData("name eq null", "[2]")]
    [InlineData("name neq null", "[1,3]")]
    [InlineData("null eq name", "[2]")]
    [InlineData("name eq 'a'", "[1]")]
    [InlineData("name neq 'a'", "[2,3]")]     // null neq 'a' is true, unlike SQL
    [InlineData("name gt 'a'", "[3]")]         // ordering never matches null
    [InlineData("name gte 'a'", "[1,3]")]
    [InlineData("name lt 'c'", "[1]")]
    [InlineData("name lte 'c'", "[1,3]")]
    [InlineData("name eq name", "[1,2,3]")]    // null equals null
    public void NullFollowsTwoValuedLogic(string predicate, string expectedIds)
    {
        var json = Run($"$source({WithNull}).$filter({predicate})");

        var ids = string.Join(",", ExtractIds(json));

        Assert.Equal(expectedIds, "[" + ids + "]");
    }

    [Fact]
    public void NullValuesAreWrittenAsJsonNull()
    {
        Assert.Equal(
            """[{"id":2,"name":null}]""",
            Run($"$source({WithNull}).$filter(name eq null)"));
    }

    [Fact]
    public void ComparingLiterals_WithNull()
    {
        Assert.Equal("""[{"id":1}]""", Run("$source([{id: 1}]).$filter(null eq null)"));
        Assert.Equal("[]", Run("$source([{id: 1}]).$filter(1 eq null)"));
        Assert.Equal("""[{"id":1}]""", Run("$source([{id: 1}]).$filter(1 neq null)"));
    }

    [Fact]
    public void ANullBooleanCountsAsFalse()
    {
        const string data = "[{a: true}, {a: null}]";

        Assert.Equal("""[{"a":true}]""", Run($"$source({data}).$filter(a)"));
        Assert.Equal("""[{"a":true}]""", Run($"$source({data}).$filter(a or false)"));
        Assert.Equal("""[{"a":true}]""", Run($"$source({data}).$filter(a and true)"));
    }

    private static IEnumerable<string> ExtractIds(string json)
    {
        var rest = json;

        while (true)
        {
            var at = rest.IndexOf("\"id\":", StringComparison.Ordinal);

            if (at < 0)
                yield break;

            rest = rest[(at + 5)..];
            var end = rest.IndexOfAny([',', '}']);

            yield return rest[..end];
        }
    }

    // ------------------------------------------------------------------
    // Logical operators and chains
    // ------------------------------------------------------------------

    private const string Four = "[{id: 1}, {id: 2}, {id: 3}, {id: 4}]";

    [Theory]
    [InlineData("id gt 1 and id lt 4", """[{"id":2},{"id":3}]""")]
    [InlineData("id eq 1 or id eq 4", """[{"id":1},{"id":4}]""")]
    [InlineData("id eq 1 or id gt 1 and id lt 3", """[{"id":1},{"id":2}]""")]     // and binds tighter
    [InlineData("(id eq 1 or id eq 2) and id gt 1", """[{"id":2}]""")]
    [InlineData("id gt 1 and id gt 2 and id gt 3", """[{"id":4}]""")]
    public void LogicalOperators_FollowPrecedence(string predicate, string expected)
    {
        Assert.Equal(expected, Run($"$source({Four}).$filter({predicate})"));
    }

    [Fact]
    public void FilterCanBeChained()
    {
        Assert.Equal(
            """[{"id":2},{"id":3}]""",
            Run($"$source({Four}).$filter(id gt 1).$filter(id lt 4)"));
    }

    [Fact]
    public void ComparingTwoLiterals_IsAConstantPredicate()
    {
        Assert.Equal(Run($"$source({Four})"), Run($"$source({Four}).$filter(1 eq 1)"));
        Assert.Equal("[]", Run($"$source({Four}).$filter(1 eq 2)"));
        Assert.Equal(Run($"$source({Four})"), Run($"$source({Four}).$filter('a' lt 'b')"));
    }

    // ------------------------------------------------------------------
    // Nested values survive a round trip
    // ------------------------------------------------------------------

    [Fact]
    public void ArraysAndObjectsInsideRows_AreKept()
    {
        Assert.Equal(
            """[{"id":1,"tags":["a","b"],"meta":{"x":1.5,"y":null}}]""",
            Run("$source([{id: 1, tags: ['a', 'b'], meta: {x: 1.5, y: null}}]).$filter(id eq 1)"));
    }

    [Fact]
    public void RowsMayListTheirFieldsInADifferentOrder()
    {
        Assert.Equal(
            """[{"id":1,"name":"a"},{"name":"b","id":2}]""",
            Run("$source([{id: 1, name: 'a'}, {name: 'b', id: 2}]).$filter(id gt 0)"));

        Assert.Equal(
            """[{"name":"b","id":2}]""",
            Run("$source([{id: 1, name: 'a'}, {name: 'b', id: 2}]).$filter(name eq 'b')"));
    }

    // ------------------------------------------------------------------
    // Result object
    // ------------------------------------------------------------------

    [Fact]
    public void Result_ExposesTheSemanticTypeAndTheRuntimeValue()
    {
        var result = Engine.Execute($"$source({Hello}).$filter(id eq 2)");

        Assert.Equal("array", result.Type.Name);

        var rows = Assert.IsType<List<object?>>(result.Value);
        var row = Assert.IsType<ObjectValue>(Assert.Single(rows));

        Assert.True(row.TryGetValue("id", out var id));
        Assert.Equal(2, id);
        Assert.True(row.TryGetValue("name", out var name));
        Assert.Equal("world", name);
        Assert.False(row.TryGetValue("missing", out _));
    }

    [Fact]
    public void NumbersKeepTheirTypeAtRuntime()
    {
        var result = Engine.Execute("$source([{i: 1, l: 2147483648, d: 1.5}])");

        var rows = Assert.IsType<List<object?>>(result.Value);
        var row = Assert.IsType<ObjectValue>(Assert.Single(rows));

        Assert.IsType<int>(row.GetValueAt(0));
        Assert.IsType<long>(row.GetValueAt(1));
        Assert.IsType<decimal>(row.GetValueAt(2));
    }

    [Fact]
    public void RunningTheSameQueryTwice_GivesTheSameResult()
    {
        var query = $"$source({Hello}).$filter(id gte 1)";

        Assert.Equal(Run(query), Run(query));
        Assert.Equal(Run(query), new QueryEngine().Execute(query).ToJson());
    }

    // ------------------------------------------------------------------
    // Errors go through the engine unchanged
    // ------------------------------------------------------------------

    [Fact]
    public void InvalidQueries_FailWithTheirCodeAndPosition()
    {
        QueryAssert.Fails(QueryErrorCode.UnknownFunction, 0, 5, () => Engine.Execute("$nope(1)"));
        QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            26,
            9,
            () => Engine.Execute("$source([{id:1}]).$filter(id eq 'a')"));
    }

    [Fact]
    public void LimitsGivenToTheEngine_AreEnforcedInEveryStage()
    {
        var strict = new QueryEngine(new QueryLimits { MaxInputLength = 20 });

        QueryAssert.Fails(QueryErrorCode.InputTooLong, () => strict.Execute($"$source({Hello})"));
        Assert.Equal(20, strict.Limits.MaxInputLength);
    }

    [Fact]
    public void ANullQuery_IsAProgrammingError_NotAQueryError()
    {
        Assert.Throws<ArgumentNullException>(() => { Engine.Execute(null!); });
    }

    // ------------------------------------------------------------------
    // Extending the engine
    // ------------------------------------------------------------------

    private static FunctionDefinition Define(string name) => new()
    {
        Name = name,
        Parameters = [],
        TargetRule = TargetRule.Required,
        AcceptsTarget = t => t is ArrayType,
        ReturnType = target => target ?? SemanticTypeOptions.Unknown,
    };

    [Fact]
    public void AFunctionWithoutAnInMemoryImplementation_IsRejectedNotIgnored()
    {
        var functions = QueryPipeline.CoreRegistry();
        functions.Add(Define("$noop"));
        var engine = new QueryEngine(functions: functions);

        QueryAssert.Fails(
            QueryErrorCode.NotSupported,
            18,
            5,
            () => engine.Execute("$source([{id:1}]).$noop()"));
    }

    [Fact]
    public void AnExtensionCanAddAFunctionAndItsImplementation()
    {
        var functions = QueryPipeline.CoreRegistry();
        functions.Add(new FunctionDefinition
        {
            Name = "$count",
            Parameters = [],
            TargetRule = TargetRule.Required,
            AcceptsTarget = t => t is ArrayType,
            ReturnType = _ => SemanticTypeOptions.Int,
        });

        var implementations = InMemoryFunctions.CreateDefault().Add(
            "$count",
            (compiler, call) =>
            {
                var target = compiler.Compile(call.Target!);

                return element => ((IEnumerable<object?>)target(element)!).Count();
            });

        var engine = new QueryEngine(functions: functions, implementations: implementations);
        var result = engine.Execute($"$source({Four}).$filter(id gt 1).$count()");

        Assert.Equal("int", result.Type.Name);
        Assert.Equal(3, result.Value);
        Assert.Equal("3", result.ToJson());
    }

    [Fact]
    public void InMemoryImplementations_CannotBeRegisteredTwice()
    {
        Assert.Throws<InvalidOperationException>(
            () => { InMemoryFunctions.CreateDefault().Add("$source", (c, call) => c.Compile(call)); });
    }
}
