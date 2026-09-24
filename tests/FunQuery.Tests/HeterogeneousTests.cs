using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>Numeric widening and objects with different field sets (docs/DataTypes.md#objects).</summary>
public class HeterogeneousTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // Numeric widening: int < long < decimal
    // ------------------------------------------------------------------

    private static string ElementTypeOf(string literal)
    {
        var result = QueryPipeline.Analyze($"$source([{literal}])");

        return Assert.IsType<ArrayType>(result.SemanticType).Type.Name;
    }

    [Theory]
    [InlineData("1, 2", "int")]
    [InlineData("1, 2147483648", "long")]
    [InlineData("1, 1.5", "decimal")]
    [InlineData("2147483648, 1.5", "decimal")]
    [InlineData("1, 2147483648, 1.5", "decimal")]
    [InlineData("1.5, 1", "decimal")]
    public void MixedNumericLiterals_WidenToTheirCommonType(string elements, string expected)
    {
        Assert.Equal(expected, ElementTypeOf(elements));
    }

    [Fact]
    public void WideningWorksInsideObjectFieldsToo()
    {
        var result = QueryPipeline.Analyze("$source([{n: 1}, {n: 1.5}])");

        var array = Assert.IsType<ArrayType>(result.SemanticType);
        var element = Assert.IsType<ObjectType>(array.Type);

        Assert.Equal("decimal", element.Fields["n"].Name);
    }

    [Fact]
    public void WideningWorksInsideNestedArrays()
    {
        var result = QueryPipeline.Analyze("$source([[1], [1.5]])");

        var outer = Assert.IsType<ArrayType>(result.SemanticType);
        var inner = Assert.IsType<ArrayType>(outer.Type);

        Assert.Equal("decimal", inner.Type.Name);
    }

    [Fact]
    public void WidenedArraysCompareCorrectlyAtRuntime()
    {
        Assert.Equal(
            "[1.5]",
            Run("$source([1, 1.5, 2147483648]).$filter(~ gt 1 and ~ lt 2)"));
    }

    // ------------------------------------------------------------------
    // Objects with different field sets unify; only real type conflicts fail
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("{id: 1}, {name: 'x'}")]
    [InlineData("{id: 1, name: 'a'}, {id: 2}")]
    [InlineData("{a: 1}, {b: 2}, {c: 3}")]
    [InlineData("{a: {x: 1}}, {a: {y: 2}}")]
    public void ObjectsWithDifferentFieldSets_Unify(string elements)
    {
        Assert.NotNull(QueryPipeline.Analyze($"$source([{elements}])").SemanticType);
    }

    [Theory]
    [InlineData("{id: 1}, {id: 'x'}")]
    [InlineData("{id: 1, name: 'a'}, {id: 'x', name: 1}")]
    [InlineData("{a: {x: 1}}, {a: {x: 'y'}}")]
    public void ObjectsWithAGenuineFieldTypeConflict_AreStillRejected(string elements)
    {
        QueryAssert.Fails(
            QueryErrorCode.IncompatibleElementTypes,
            () => QueryPipeline.Analyze($"$source([{elements}])"));
    }

    [Fact]
    public void AFieldPresentInOnlySomeRows_UnifiesWithItsOwnType_NotWithNull()
    {
        var result = QueryPipeline.Analyze("$source([{id: 1, name: 'a'}, {id: 2}])");

        var array = Assert.IsType<ArrayType>(result.SemanticType);
        var element = Assert.IsType<ObjectType>(array.Type);

        Assert.Equal("string", element.Fields["name"].Name);
    }

    // ------------------------------------------------------------------
    // Execution: missing fields read as null, values are kept per row
    // ------------------------------------------------------------------

    [Fact]
    public void RowsWithDifferentFields_KeepTheirOwnFieldsInTheOutput()
    {
        Assert.Equal(
            """[{"id":1,"name":"a"},{"id":2}]""",
            Run("$source([{id: 1, name: 'a'}, {id: 2}]).$filter(id gt 0)"));
    }

    [Fact]
    public void AMissingFieldReadsAsNull_AndCanBeFilteredOn()
    {
        const string data = "[{id: 1, name: 'a'}, {id: 2}]";

        Assert.Equal("""[{"id":2}]""", Run($"$source({data}).$filter(name eq null)"));
        Assert.Equal("""[{"id":1,"name":"a"}]""", Run($"$source({data}).$filter(name neq null)"));
    }

    [Fact]
    public void WidenedNumbersKeepTheirOriginalRuntimeValuePerRow()
    {
        // The static element type is "decimal", but each row's own literal value and CLR
        // type are unchanged; only comparisons treat them uniformly.
        var result = Engine.Execute("$source([{n: 1}, {n: 1.5}])");

        var rows = Assert.IsType<List<object?>>(result.Value);

        Assert.IsType<int>(((FunQuery.Execution.ObjectValue)rows[0]!).GetValueAt(0));
        Assert.IsType<decimal>(((FunQuery.Execution.ObjectValue)rows[1]!).GetValueAt(0));
    }

    [Fact]
    public void HeterogeneousObjectsCombineWithFieldPathsAndTilde()
    {
        const string data = "[{id: 1, addr: {city: 'x'}}, {id: 2}]";

        Assert.Equal(
            """[{"id":1,"addr":{"city":"x"}}]""",
            Run($"$source({data}).$filter(addr.city eq 'x')"));

        Assert.Equal(
            """[{"id":2}]""",
            Run($"$source({data}).$filter(addr.city eq null)"));
    }
}
