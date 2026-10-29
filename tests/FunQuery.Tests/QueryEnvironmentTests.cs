using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.SemanticTypes;
using FunQuery.Tests.Support;

namespace FunQuery.Tests;

/// <summary>
/// Typed parameters and the prepare/execute split. Contract in docs/Milestone5.md (M5.3).
/// </summary>
public class QueryEnvironmentTests
{
    private static readonly QueryEngine Engine = new();

    private static ObjectType Schema(params (string Name, SemanticType Type)[] fields)
    {
        var map = new Dictionary<string, SemanticType>(StringComparer.Ordinal);

        foreach (var (name, type) in fields)
            map[name] = type;

        return new ObjectType(map);
    }

    [Fact]
    public void AnEnvironmentParameterIsAvailableAsAVariable()
    {
        var environment = QueryEnvironment.From(
            "items",
            new QueryParameter(
                SemanticTypeOptions.Array(SemanticTypeOptions.Int),
                new List<object?> { 1, 2, 3 }));

        Assert.Equal(
            "3",
            Engine.Execute("$source(@items).$count()", environment).ToJson());
    }

    [Fact]
    public void TheParameterTypeDrivesAnalysis()
    {
        var environment = QueryEnvironment.From(
            "min",
            new QueryParameter(SemanticTypeOptions.Int, 2));

        Assert.Equal(
            "[{\"id\":3}]",
            Engine.Execute(
                "$source([{id: 1}, {id: 2}, {id: 3}]).$filter(id gt @min)",
                environment).ToJson());
    }

    [Fact]
    public void ARequestParameterCannotBeRedefinedByLet()
    {
        var environment = QueryEnvironment.From(
            "min",
            new QueryParameter(SemanticTypeOptions.Int, 1));

        QueryAssert.Fails(
            QueryErrorCode.VariableRedefined,
            () => Engine.Execute("$let(@min, 2).$source([1])", environment));
    }

    [Fact]
    public void PrepareFailsBeforeAnyValueIsNeededWhenTheSchemaHasNoSuchField()
    {
        var types = QueryEnvironment.From(
            "source",
            new QueryParameter(new ArrayType(Schema(("id", SemanticTypeOptions.Int))), null));

        QueryAssert.Fails(
            QueryErrorCode.UnknownIdentifier,
            () => Engine.Prepare("$source(@source).$filter(nope eq 1)", types));
    }

    [Fact]
    public void APreparedPlanExecutesAgainstValuesLater()
    {
        var schema = Schema(("id", SemanticTypeOptions.Int));

        var types = QueryEnvironment.From(
            "source",
            new QueryParameter(new ArrayType(schema), null));

        var plan = Engine.Prepare("$source(@source).$count()", types);

        var rows = SourceRows.Materialize(
            "source",
            schema,
            [
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = 1 },
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = 2 },
            ]);

        var values = QueryEnvironment.From(
            "source",
            new QueryParameter(new ArrayType(schema), rows));

        Assert.Equal("2", Engine.ExecutePlan(plan, values).ToJson());
    }

    [Fact]
    public void TheEmptyEnvironmentIsUsable()
    {
        Assert.Empty(QueryEnvironment.Empty.Parameters);

        Assert.Equal(
            "1",
            Engine.Execute("$source([1]).$count()", QueryEnvironment.Empty).ToJson());
    }
}
