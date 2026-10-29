using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Sql;
using FunQuery.Sql.Plan;

namespace FunQuery.Sql.Tests;

/// <summary>M6.1: capabilities reject what a provider cannot translate.</summary>
public class PlanValidatorTests
{
    private static RelationalPlan Build(
        string query,
        QueryEnvironment? environment = null,
        IReadOnlyDictionary<string, PlanSource>? sources = null)
    {
        var ast = new QueryEngine().Prepare(query, environment).Expression;

        return PlanBuilder.Build(
            query,
            ast,
            sources ?? new Dictionary<string, PlanSource>(),
            environment);
    }

    private static void AssertRejected(
        RelationalPlan plan,
        ProviderCapabilities capabilities)
    {
        var error = Assert.Throws<QueryException>(
            () => PlanValidator.Validate(plan, capabilities));

        Assert.Equal(QueryErrorCode.NotSupported, error.Code);
    }

    private static void AssertRejected(
        string query,
        ProviderCapabilities capabilities) =>
        AssertRejected(Build(query), capabilities);

    [Fact]
    public void AllCapabilitiesAcceptACompletePlan()
    {
        var plan = Build(
            "$source([1, 2, 3]).$filter(~ gt 1).$sort(~).$take(2)");

        PlanValidator.Validate(plan, ProviderCapabilities.All);
    }

    [Fact]
    public void FilteringIsRequiredForFilter()
    {
        AssertRejected(
            "$source([1, 2]).$filter(~ gt 1)",
            ProviderCapabilities.All & ~ProviderCapabilities.Filtering);
    }

    [Fact]
    public void InlineSourcesAreRequiredForValues()
    {
        AssertRejected(
            "$source([1, 2]).$count()",
            ProviderCapabilities.All & ~ProviderCapabilities.InlineSources);
    }

    [Fact]
    public void WindowFunctionsAreRequiredForIndex()
    {
        AssertRejected(
            "$source([1]).$map($index())",
            ProviderCapabilities.All & ~ProviderCapabilities.WindowFunctions);
    }

    [Fact]
    public void DistinctOnIsRequiredForKeyedDistinct()
    {
        AssertRejected(
            "$source([{g: 'a'}]).$distinct(g)",
            ProviderCapabilities.All & ~ProviderCapabilities.DistinctOn);
    }

    [Fact]
    public void StringOperationsAreRequiredForLower()
    {
        AssertRejected(
            "$source(['A']).$map($lower(~))",
            ProviderCapabilities.All & ~ProviderCapabilities.StringOperations);
    }

    [Fact]
    public void DateOperationsAreRequiredForDate()
    {
        AssertRejected(
            "$source([{d: '2024-01-01'}]).$map($date(d))",
            ProviderCapabilities.All & ~ProviderCapabilities.DateOperations);
    }

    [Fact]
    public void PaginationIsRequiredForTake()
    {
        AssertRejected(
            "$source([1, 2]).$take(1)",
            ProviderCapabilities.All & ~ProviderCapabilities.Pagination);
    }

    [Fact]
    public void AggregationIsRequiredForCount()
    {
        AssertRejected(
            "$source([1, 2]).$count()",
            ProviderCapabilities.All & ~ProviderCapabilities.Aggregation);
    }

    [Fact]
    public void JsonProjectionIsRequiredForNestedObjects()
    {
        AssertRejected(
            "$source([1]).$map({row: {value: ~}})",
            ProviderCapabilities.All & ~ProviderCapabilities.JsonProjection);
    }

    [Fact]
    public void InListsDoNotRequireJsonProjection()
    {
        var plan = Build("$source([1, 2]).$filter(~ in [1])");

        PlanValidator.Validate(
            plan,
            ProviderCapabilities.All & ~ProviderCapabilities.JsonProjection);
    }

    [Fact]
    public void ObjectRowProjectionRequiresJsonProjection()
    {
        var schema = new ObjectType(
            new Dictionary<string, SemanticType>(StringComparer.Ordinal)
            {
                ["id"] = SemanticTypeOptions.Int,
            });

        var environment = QueryEnvironment.From(
            "source",
            new QueryParameter(new ArrayType(schema), null));

        var sources = new Dictionary<string, PlanSource>(StringComparer.Ordinal)
        {
            ["source"] = new PlanSource("orders", schema),
        };

        var plan = Build("$source(@source).$select({row: ~})", environment, sources);

        AssertRejected(
            plan,
            ProviderCapabilities.All & ~ProviderCapabilities.JsonProjection);
    }
}
