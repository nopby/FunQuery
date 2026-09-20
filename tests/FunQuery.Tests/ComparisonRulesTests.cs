using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>
/// Operator rules from docs/Operators.md. There are no float or double literals in v1, so the rows come
/// from a test-only function, the same way a provider schema would expose them.
/// </summary>
public class ComparisonRulesTests
{
    private static readonly SemanticType Rows = new ArrayType(new ObjectType(
        new Dictionary<string, SemanticType>
        {
            ["id"] = SemanticTypeOptions.Int,
            ["big"] = SemanticTypeOptions.Long,
            ["amount"] = SemanticTypeOptions.Decimal,
            ["price"] = SemanticTypeOptions.Double,
            ["ratio"] = SemanticTypeOptions.Float,
            ["name"] = SemanticTypeOptions.String,
            ["active"] = SemanticTypeOptions.Boolean,
            ["tags"] = SemanticTypeOptions.Array(SemanticTypeOptions.String),
            ["meta"] = new ObjectType(new Dictionary<string, SemanticType>
            {
                ["a"] = SemanticTypeOptions.Int,
            }),
        }));

    private sealed class RowsExtension : IQueryExtension
    {
        public string Name => "rows";

        public void Register(FunctionRegistry registry) =>
            registry.Add(new FunctionDefinition
            {
                Name = "$rows",
                Parameters = [],
                TargetRule = TargetRule.Forbidden,
                ReturnType = _ => Rows,
            });
    }

    // "$rows().$filter(" is 16 characters, so the predicate starts at position 16.
    private const int PredicateStart = 16;

    private static BaseExpression Analyze(string predicate) =>
        QueryPipeline.Analyze(
            "$rows().$filter(" + predicate + ")",
            functions: QueryPipeline.CoreRegistry().AddExtension(new RowsExtension()));

    private static QueryException Fails(string predicate) =>
        QueryAssert.Fails(QueryErrorCode.TypeMismatch, () => Analyze(predicate));

    // ------------------------------------------------------------------
    // float and double: equality is rejected, ordering is allowed
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("price eq 1.5")]
    [InlineData("price neq 1.5")]
    [InlineData("1.5 eq price")]
    [InlineData("1.5 neq price")]
    [InlineData("ratio eq ratio")]
    [InlineData("price eq ratio")]
    [InlineData("id eq price")]
    [InlineData("price neq big")]
    [InlineData("amount eq ratio")]
    public void EqualityOnFloatOrDouble_IsRejected(string predicate)
    {
        var error = Fails(predicate);

        Assert.Contains("float and double", error.Message);
        Assert.Equal(PredicateStart, error.Position);
        Assert.Equal(predicate.Length, error.Length);
    }

    [Theory]
    [InlineData("price gt 1.5")]
    [InlineData("price gte 1")]
    [InlineData("price lt 2")]
    [InlineData("ratio lte 2.5")]
    [InlineData("price gt ratio")]
    [InlineData("price lte big")]
    [InlineData("1.5 lt price")]
    public void OrderingOnFloatOrDouble_IsAccepted(string predicate)
    {
        Assert.NotNull(Analyze(predicate).SemanticType);
    }

    // ------------------------------------------------------------------
    // int, long, decimal
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("id eq 1")]
    [InlineData("id neq big")]
    [InlineData("big eq amount")]
    [InlineData("amount neq 1")]
    [InlineData("amount eq 1.5")]
    [InlineData("id lt 99999999999")]
    [InlineData("id gte big")]
    [InlineData("amount lte id")]
    [InlineData("1 eq 1")]
    public void NumericTypes_CanBeMixedInEveryComparison(string predicate)
    {
        Assert.NotNull(Analyze(predicate).SemanticType);
    }

    // ------------------------------------------------------------------
    // string and bool
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("name eq 'a'")]
    [InlineData("name neq 'a'")]
    [InlineData("name gt 'a'")]
    [InlineData("name gte 'a'")]
    [InlineData("name lt 'a'")]
    [InlineData("name lte 'a'")]
    public void Strings_SupportEveryComparison(string predicate)
    {
        Assert.NotNull(Analyze(predicate).SemanticType);
    }

    [Theory]
    [InlineData("name gt 1")]
    [InlineData("id eq 'x'")]
    [InlineData("name eq active")]
    [InlineData("id eq active")]
    public void DifferentTypeFamilies_CannotBeCompared(string predicate)
    {
        Assert.Contains("Cannot compare", Fails(predicate).Message);
    }

    [Theory]
    [InlineData("active eq active")]
    [InlineData("active neq active")]
    public void Booleans_CanBeTestedForEquality(string predicate)
    {
        Assert.NotNull(Analyze(predicate).SemanticType);
    }

    [Theory]
    [InlineData("gt")]
    [InlineData("gte")]
    [InlineData("lt")]
    [InlineData("lte")]
    public void Booleans_HaveNoOrder(string op)
    {
        var error = Fails("active " + op + " active");

        Assert.Contains($"'{op}' cannot be applied to bool", error.Message);
    }

    // ------------------------------------------------------------------
    // array and object are never comparable
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("tags eq tags", "array")]
    [InlineData("tags neq tags", "array")]
    [InlineData("tags gt tags", "array")]
    [InlineData("meta eq meta", "object")]
    [InlineData("meta neq meta", "object")]
    [InlineData("meta lte meta", "object")]
    public void ArraysAndObjects_CannotBeCompared(string predicate, string typeName)
    {
        var error = Fails(predicate);

        Assert.Contains($"cannot be applied to {typeName}", error.Message);
        // The error points at the offending operand, which is the first word here.
        Assert.Equal(PredicateStart, error.Position);
        Assert.Equal(4, error.Length);
    }

    [Fact]
    public void ArrayCompared_WithANumber_IsAMismatch()
    {
        Assert.Contains("Cannot compare int with array", Fails("id eq tags").Message);
    }

    // ------------------------------------------------------------------
    // syntax
    // ------------------------------------------------------------------

    [Fact]
    public void ComparisonsDoNotChain()
    {
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedToken,
            () => QueryPipeline.Parse("$filter(id eq 1 eq 2)"));
    }

    [Theory]
    [InlineData("EQ")]
    [InlineData("Neq")]
    [InlineData("GT")]
    public void OperatorWords_AreCaseSensitive(string op)
    {
        QueryAssert.Fails(
            QueryErrorCode.UnexpectedToken,
            () => QueryPipeline.Parse("$filter(id " + op + " 1)"));
    }
}
