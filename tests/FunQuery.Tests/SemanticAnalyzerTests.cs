using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

public class SemanticAnalyzerTests
{
    // Two people: id is Int, name is String.
    private const string People = "$source([{id: 1, name: 'ann'}, {id: 2, name: 'bob'}])";

    private static string Filter(string predicate) => People + ".$filter(" + predicate + ")";

    private static void AssertFails(
        QueryErrorCode expected,
        string input,
        QueryLimits? limits = null,
        FunctionRegistry? functions = null) =>
        QueryAssert.Fails(
            expected,
            () => QueryPipeline.Analyze(input, limits, functions));

    private static SemanticType ElementTypeOf(string literal)
    {
        var result = QueryPipeline.Analyze("$source([" + literal + "])");
        var array = Assert.IsType<ArrayType>(result.SemanticType);
        return array.Type;
    }

    // ------------------------------------------------------------------
    // Sample from Program.cs
    // ------------------------------------------------------------------

    [Fact]
    public void ProgramSample_IsValid()
    {
        var result = QueryPipeline.Analyze(
            "$source([{id: 1, name: 'hello'}, {id: 2, name: 'world'}]).$filter(id eq 2.1)");

        Assert.IsType<ArrayType>(result.SemanticType);
    }

    // ------------------------------------------------------------------
    // $source and literals
    // ------------------------------------------------------------------

    [Fact]
    public void Source_OfObjects_InfersAnArrayOfObjectType()
    {
        var result = QueryPipeline.Analyze(People);

        var array = Assert.IsType<ArrayType>(result.SemanticType);
        var element = Assert.IsType<ObjectType>(array.Type);
        Assert.Equal(SemanticTypeOptions.Int, element.Fields["id"]);
        Assert.Equal(SemanticTypeOptions.String, element.Fields["name"]);
    }

    [Fact]
    public void Source_Empty_IsAnArrayOfUnknown()
    {
        var result = QueryPipeline.Analyze("$source([])");

        var array = Assert.IsType<ArrayType>(result.SemanticType);
        Assert.Equal(SemanticTypeOptions.Unknown, array.Type);
    }

    [Fact]
    public void IntegerLiterals_UseTheSmallestFittingType()
    {
        Assert.Equal(SemanticTypeOptions.Int, ElementTypeOf("1"));
        Assert.Equal(SemanticTypeOptions.Int, ElementTypeOf("2147483647"));
        Assert.Equal(SemanticTypeOptions.Long, ElementTypeOf("2147483648"));
        Assert.Equal(SemanticTypeOptions.Long, ElementTypeOf("9223372036854775807"));
        Assert.Equal(SemanticTypeOptions.Decimal, ElementTypeOf("9223372036854775808"));
    }

    [Fact]
    public void DecimalAndStringLiterals_GetTheirOwnTypes()
    {
        Assert.Equal(SemanticTypeOptions.Decimal, ElementTypeOf("1.5"));
        Assert.Equal(SemanticTypeOptions.String, ElementTypeOf("'text'"));
    }

    [Fact]
    public void NumberBeyondDecimalRange_IsRejected()
    {
        // 30 digits: larger than decimal.MaxValue (29 digits).
        AssertFails(
            QueryErrorCode.NumberOutOfRange,
            "$source([100000000000000000000000000000])");
    }

    [Fact]
    public void ArrayWithMixedElementTypes_IsRejected()
    {
        AssertFails(QueryErrorCode.IncompatibleElementTypes, "$source([1, 'a'])");
    }

    [Fact]
    public void ArrayOfObjectsWithDifferentFields_IsRejected()
    {
        AssertFails(
            QueryErrorCode.IncompatibleElementTypes,
            "$source([{id: 1}, {name: 'x'}])");
    }

    [Fact]
    public void ArrayOfObjectsWithSameFieldsInDifferentOrder_IsAccepted()
    {
        var result = QueryPipeline.Analyze(
            "$source([{id: 1, name: 'a'}, {name: 'b', id: 2}])");

        Assert.IsType<ArrayType>(result.SemanticType);
    }

    [Fact]
    public void ObjectWithDuplicateField_IsRejected()
    {
        AssertFails(QueryErrorCode.DuplicateField, "$source([{id: 1, id: 2}])");
    }

    [Fact]
    public void Source_WithNonArrayArgument_IsRejected()
    {
        AssertFails(QueryErrorCode.TypeMismatch, "$source(1)");
        AssertFails(QueryErrorCode.TypeMismatch, "$source('text')");
    }

    // ------------------------------------------------------------------
    // $filter
    // ------------------------------------------------------------------

    [Fact]
    public void Filter_KeepsTheTypeOfItsTarget()
    {
        var result = QueryPipeline.Analyze(Filter("id eq 2"));

        var filter = Assert.IsType<CallExpression>(result);
        Assert.NotNull(filter.Target);
        Assert.NotNull(filter.SemanticType);
        Assert.Same(filter.Target!.SemanticType, filter.SemanticType);
    }

    [Theory]
    [InlineData("eq")]
    [InlineData("neq")]
    [InlineData("gt")]
    [InlineData("gte")]
    [InlineData("lt")]
    [InlineData("lte")]
    public void Filter_AcceptsEveryComparisonOperator(string op)
    {
        var result = QueryPipeline.Analyze(Filter("id " + op + " 1"));

        Assert.IsType<ArrayType>(result.SemanticType);
    }

    [Theory]
    [InlineData("id gt 1 and name eq 'x'")]
    [InlineData("id lt 1 or id gt 5")]
    [InlineData("(id eq 1 or id eq 2) and name neq 'x'")]
    [InlineData("id eq 2.1")]
    [InlineData("id eq id")]
    [InlineData("1 eq 1")]
    public void Filter_AcceptsWellTypedPredicates(string predicate)
    {
        var result = QueryPipeline.Analyze(Filter(predicate));

        Assert.IsType<ArrayType>(result.SemanticType);
    }

    [Fact]
    public void Filter_WithUnknownField_IsRejected()
    {
        AssertFails(QueryErrorCode.UnknownIdentifier, Filter("age gt 1"));
    }

    [Fact]
    public void FieldNames_AreCaseSensitive()
    {
        AssertFails(QueryErrorCode.UnknownIdentifier, Filter("ID eq 1"));
    }

    [Theory]
    [InlineData("id eq 'x'")]
    [InlineData("name gt 1")]
    public void Filter_ComparingIncompatibleTypes_IsRejected(string predicate)
    {
        AssertFails(QueryErrorCode.TypeMismatch, Filter(predicate));
    }

    [Theory]
    [InlineData("id and id eq 1")]
    [InlineData("id eq 1 and id")]
    [InlineData("id or id eq 1")]
    public void Filter_LogicalOperandsMustBeBoolean(string predicate)
    {
        AssertFails(QueryErrorCode.TypeMismatch, Filter(predicate));
    }

    [Fact]
    public void Filter_PredicateMustBeBoolean()
    {
        AssertFails(QueryErrorCode.TypeMismatch, Filter("id"));
    }

    [Fact]
    public void Filter_NeedsExactlyOneArgument()
    {
        AssertFails(QueryErrorCode.InvalidArgumentCount, People + ".$filter()");
        AssertFails(QueryErrorCode.InvalidArgumentCount, Filter("id eq 1, id eq 2"));
    }

    // ------------------------------------------------------------------
    // Functions and targets
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("$nope([])")]
    [InlineData("$Source([])")]
    public void UnknownFunction_IsRejected(string input)
    {
        AssertFails(QueryErrorCode.UnknownFunction, input);
    }

    [Fact]
    public void Source_NeedsExactlyOneArgument()
    {
        AssertFails(QueryErrorCode.InvalidArgumentCount, "$source()");
        AssertFails(QueryErrorCode.InvalidArgumentCount, "$source([], [])");
    }

    [Fact]
    public void Filter_CannotStartAChain()
    {
        AssertFails(QueryErrorCode.InvalidTarget, "$filter(1 eq 1)");
    }

    [Fact]
    public void Source_CannotBeChainedOntoAnotherCall()
    {
        AssertFails(QueryErrorCode.InvalidTarget, "$source([]).$source([])");
    }

    [Fact]
    public void Filter_StillRequiresAnArray_NotAScalarOrObject()
    {
        AssertFails(QueryErrorCode.InvalidTarget, "$filter(1 eq 1)");
    }

    [Fact]
    public void Filter_OnAnArrayOfScalars_HasNoNamedFields_UseTildeInstead()
    {
        // "id" is not a field of a scalar element; ~ is used instead (see TildeAndFieldAccessTests).
        AssertFails(QueryErrorCode.UnknownIdentifier, "$source([1, 2]).$filter(id eq 2)");
    }

    // ------------------------------------------------------------------
    // Depth
    // ------------------------------------------------------------------

    private static string FilterChain(int length) =>
        "$source([{id: 1}])" + string.Concat(Enumerable.Repeat(".$filter(id eq 1)", length));

    [Fact]
    public void ChainWithinMaxDepth_IsAccepted()
    {
        var limits = new QueryLimits { MaxDepth = 10 };

        var result = QueryPipeline.Analyze(FilterChain(3), limits: limits);

        Assert.IsType<ArrayType>(result.SemanticType);
    }

    [Fact]
    public void ChainBeyondMaxDepth_IsRejectedByTheAnalyzer()
    {
        // The parser only nests four levels here, so this limit is hit by the analyzer,
        // where every link of a call chain counts as one more level.
        var limits = new QueryLimits { MaxDepth = 10 };

        AssertFails(QueryErrorCode.MaxDepthExceeded, FilterChain(12), limits: limits);
    }
}
