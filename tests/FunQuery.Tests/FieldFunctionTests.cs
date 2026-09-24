using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>$field(name) and $field(@variable).</summary>
public class FieldFunctionTests
{
    private static readonly QueryEngine Engine = new();

    private static string Run(string query) => Engine.Execute(query).ToJson();

    // ------------------------------------------------------------------
    // Argument shape
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("$field()")]
    [InlineData("$field('a', 'b')")]
    public void FieldRequiresExactlyOneArgument(string call)
    {
        QueryAssert.Fails(
            QueryErrorCode.InvalidArgumentCount,
            () => QueryPipeline.Analyze($"$source([{{a:1}}]).$filter({call} eq 1)"));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("a")]
    [InlineData("a.b")]
    public void FieldsArgumentMustBeAStringLiteralOrAVariable(string argument)
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidFieldArgument,
            () => QueryPipeline.Analyze($"$source([{{a:1}}]).$filter($field({argument}) eq 1)"));

        Assert.Contains("string literal or a variable", error.Message);
    }

    // ------------------------------------------------------------------
    // Target rule: same as $source, transparent to $let
    // ------------------------------------------------------------------

    [Fact]
    public void FieldCannotBeCalledOnATarget()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidTarget,
            () => QueryPipeline.Analyze("$source([{id:1}]).$field('id')"));

        Assert.Contains("must start the chain", error.Message);
    }

    [Fact]
    public void FieldIsTransparentToALeadingLet()
    {
        // $field itself still needs an active element scope, so this fails on that, not on
        // the (irrelevant) presence of $let before it.
        var error = QueryAssert.Fails(
            QueryErrorCode.ItemOutOfContext,
            () => QueryPipeline.Analyze("$let(@x, 1).$field('x')"));

        Assert.DoesNotContain("target", error.Message);
    }

    [Fact]
    public void FieldAtTheTopLevel_HasNoElementScope()
    {
        // $field('a') alone is syntactically a valid call (top-level rule is satisfied),
        // but there is no element to read a field from.
        QueryAssert.Fails(QueryErrorCode.ItemOutOfContext, () => QueryPipeline.Analyze("$field('a')"));
    }

    [Fact]
    public void FieldOutsideAnElementScope_IsItemOutOfContext()
    {
        QueryAssert.Fails(
            QueryErrorCode.ItemOutOfContext,
            () => QueryPipeline.Analyze("$source([1]).$let(@x, $field('a'))"));
    }

    // ------------------------------------------------------------------
    // Static string literal: resolved at analysis time
    // ------------------------------------------------------------------

    [Fact]
    public void FieldWithAReservedWordName_ResolvesTheField()
    {
        // "in" cannot be written as a bare identifier (it is an operator keyword), but a row
        // can still have a field named "in" via a quoted key, and $field can read it.
        var result = QueryPipeline.Analyze("$source([{'in': 1}]).$filter($field('in') eq 1)");

        Assert.Equal("array", result.SemanticType!.Name);
    }

    [Fact]
    public void FieldWithADottedPath_ResolvesANestedField()
    {
        var result = QueryPipeline.Analyze(
            "$source([{addr: {city: 'x'}}]).$filter($field('addr.city') eq 'x')");

        Assert.Equal("array", result.SemanticType!.Name);
    }

    [Fact]
    public void FieldAndPlainPathSyntax_AgreeOnTheType()
    {
        const string source = "$source([{addr: {city: 'x'}}])";

        var viaField = QueryPipeline.Analyze($"{source}.$filter($field('addr.city') eq 'x')");
        var viaPath = QueryPipeline.Analyze($"{source}.$filter(addr.city eq 'x')");

        Assert.Equal(viaField.SemanticType!.Name, viaPath.SemanticType!.Name);
    }

    [Theory]
    [InlineData("$field('missing')", QueryErrorCode.UnknownIdentifier, "'missing'")]
    [InlineData("$field('addr.missing')", QueryErrorCode.UnknownIdentifier, "'addr'")]
    public void UnknownFieldOrRoot_IsReported(string call, QueryErrorCode code, string mention)
    {
        var error = QueryAssert.Fails(
            code,
            () => QueryPipeline.Analyze($"$source([{{id:1}}]).$filter({call} eq 1)"));

        Assert.Contains(mention, error.Message);
    }

    [Fact]
    public void FieldPathThroughANonObjectValue_IsATypeMismatch()
    {
        var error = QueryAssert.Fails(
            QueryErrorCode.TypeMismatch,
            () => QueryPipeline.Analyze("$source([{id:1}]).$filter($field('id.x') eq 1)"));

        Assert.Contains("Cannot access field 'x' on int", error.Message);
    }

    [Theory]
    [InlineData("''")]
    [InlineData("'.a'")]
    [InlineData("'a.'")]
    [InlineData("'a..b'")]
    public void AMalformedPath_IsInvalidFieldArgument(string literal)
    {
        // "a" must itself be an object for the trailing/inner empty segment to even be reached
        // rather than failing earlier as an unrelated "unknown field" or "not an object" error.
        QueryAssert.Fails(
            QueryErrorCode.InvalidFieldArgument,
            () => QueryPipeline.Analyze($"$source([{{a: {{x: 1}}}}]).$filter($field({literal}) eq 1)"));
    }

    // ------------------------------------------------------------------
    // Variable argument: dynamic, resolved to AnyType at analysis time
    // ------------------------------------------------------------------

    [Fact]
    public void FieldWithAVariable_HasAnyTypeAtAnalysisTime()
    {
        // The value isn't known until execution, so the analyzer permits it broadly, the same
        // way it permits any provider-supplied "any" typed value.
        var result = QueryPipeline.Analyze(
            "$let(@col, 'id').$source([{id: 1}]).$filter($field(@col) eq 1)");

        Assert.Equal("array", result.SemanticType!.Name);
    }

    [Fact]
    public void FieldWithAnUndefinedVariable_IsUndefinedVariable()
    {
        QueryAssert.Fails(
            QueryErrorCode.UndefinedVariable,
            () => QueryPipeline.Analyze("$source([{id:1}]).$filter($field(@missing) eq 1)"));
    }

    // ------------------------------------------------------------------
    // Execution
    // ------------------------------------------------------------------

    [Fact]
    public void FieldReadsAReservedWordNamedField()
    {
        Assert.Equal(
            """[{"id":1,"in":2}]""",
            Run("$source([{id: 1, 'in': 2}, {id: 2, 'in': 3}]).$filter($field('in') eq 2)"));
    }

    [Fact]
    public void FieldReadsANestedPath()
    {
        const string data =
            "[{id: 1, addr: {city: 'Jakarta'}}, {id: 2, addr: {city: 'Bandung'}}]";

        Assert.Equal(
            """[{"id":1,"addr":{"city":"Jakarta"}}]""",
            Run($"$source({data}).$filter($field('addr.city') eq 'Jakarta')"));
    }

    [Fact]
    public void FieldWithADynamicColumnName_ReadsTheBoundVariablesValue()
    {
        Assert.Equal(
            """[{"id":1}]""",
            Run("$let(@col, 'id').$source([{id: 1}, {id: 2}]).$filter($field(@col) eq 1)"));
    }

    [Fact]
    public void FieldWithADynamicDottedPath_ReadsTheNestedValue()
    {
        const string data = "[{addr: {city: 'Jakarta'}}, {addr: {city: 'Bandung'}}]";

        Assert.Equal(
            """[{"addr":{"city":"Jakarta"}}]""",
            Run($"$let(@col, 'addr.city').$source({data}).$filter($field(@col) eq 'Jakarta')"));
    }

    [Fact]
    public void FieldWithAMissingSegmentAtRuntime_ReadsAsNull()
    {
        // Row 2 lacks "addr" entirely (heterogeneous rows); $field must not throw, only null.
        const string data = "[{id: 1, addr: {city: 'x'}}, {id: 2}]";

        Assert.Equal(
            """[{"id":2}]""",
            Run($"$source({data}).$filter($field('addr.city') eq null)"));
    }

    [Fact]
    public void ANonStringVariableGivenToField_IsARuntimeInvalidFieldArgument()
    {
        // The analyzer cannot see the variable's concrete value, so this fails only when compiled.
        var error = QueryAssert.Fails(
            QueryErrorCode.InvalidFieldArgument,
            () => Engine.Execute("$let(@col, 1).$source([{id: 1}]).$filter($field(@col) eq 1)"));

        Assert.Contains("must resolve to a string", error.Message);
    }
}
