using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Sql;
using FunQuery.Sql.Plan;

namespace FunQuery.Sql.Tests;

/// <summary>M6.1: annotated AST → provider-neutral relational plan.</summary>
public class PlanBuilderTests
{
    private static readonly ObjectType OrderSchema = Schema(
        ("id", SemanticTypeOptions.Int),
        ("customer", SemanticTypeOptions.String),
        ("total", SemanticTypeOptions.Decimal));

    private static ObjectType Schema(params (string Name, SemanticType Type)[] fields)
    {
        var map = new Dictionary<string, SemanticType>(StringComparer.Ordinal);

        foreach (var (name, type) in fields)
            map[name] = type;

        return new ObjectType(map);
    }

    private static QueryEnvironment SourceEnvironment(ObjectType schema) =>
        QueryEnvironment.From(
            "source",
            new QueryParameter(new ArrayType(schema), null));

    private static Dictionary<string, PlanSource> Sources(ObjectType schema) =>
        new(StringComparer.Ordinal)
        {
            ["source"] = new PlanSource("orders", schema),
        };

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

    [Fact]
    public void BuildsScanFilterAndProject()
    {
        var environment = SourceEnvironment(OrderSchema);

        var plan = Build(
            "$source(@source).$filter(id gt 1).$select(id, customer)",
            environment,
            Sources(OrderSchema));

        var project = Assert.IsType<Project>(plan);
        var filter = Assert.IsType<Filter>(project.Input);
        var scan = Assert.IsType<Scan>(filter.Input);

        Assert.Equal("orders", scan.SourceName);
        Assert.True(project.ProducesObject);
        Assert.Equal(new[] { "id", "customer" }, project.Projections.Select(p => p.Key));

        var binary = Assert.IsType<Binary>(filter.Predicate);
        Assert.Equal(ComparisonOperator.GreaterThan, binary.Operator);
        Assert.Equal("id", Assert.IsType<Column>(binary.Left).Path);
        Assert.Equal(1, Assert.IsType<Literal>(binary.Right).Value);
    }

    [Fact]
    public void InlineSourceBecomesValues()
    {
        var plan = Build("$source([1, 2, 3]).$count()");

        var aggregate = Assert.IsType<Aggregate>(plan);
        Assert.Equal(AggregateFunction.Count, aggregate.Function);

        var values = Assert.IsType<Values>(aggregate.Input);
        Assert.Equal(3, values.Rows.Count);
        Assert.Equal("int", values.RowType.Name);
    }

    [Fact]
    public void MapProjectsASingleExpression()
    {
        var plan = Build("$source([1, 2]).$map(~ gt 1)");

        var project = Assert.IsType<Project>(plan);
        Assert.False(project.ProducesObject);

        var projection = Assert.Single(project.Projections);
        Assert.Null(projection.Key);
        Assert.IsType<Binary>(projection.Expression);
    }

    [Fact]
    public void SelectObjectFormUsesItsKeys()
    {
        var plan = Build(
            "$source([{id: 1, name: 'a'}]).$select({row: ~, label: name})");

        var project = Assert.IsType<Project>(plan);

        Assert.True(project.ProducesObject);
        Assert.Equal(new[] { "row", "label" }, project.Projections.Select(p => p.Key));
        Assert.IsType<CurrentRow>(project.Projections[0].Expression);
    }

    [Fact]
    public void SelectListFormUsesTheLastPathSegmentAsKey()
    {
        var plan = Build(
            "$source([{addr: {city: 'x'}}]).$select(addr.city)");

        var project = Assert.IsType<Project>(plan);
        var projection = Assert.Single(project.Projections);

        Assert.Equal("city", projection.Key);
        Assert.Equal("addr.city", Assert.IsType<Column>(projection.Expression).Path);
    }

    [Fact]
    public void SortTakeAndSkipAreExplicitNodes()
    {
        var take = Assert.IsType<Take>(
            Build("$source([3, 1, 2]).$sort(~, desc).$take(2)"));

        Assert.Equal(2, Assert.IsType<Literal>(take.Count).Value);

        var sort = Assert.IsType<Sort>(take.Input);
        Assert.True(sort.Descending);
        Assert.IsType<CurrentRow>(sort.Key);
        Assert.IsType<Values>(sort.Input);

        var skip = Assert.IsType<Skip>(Build("$source([1, 2, 3]).$skip(1)"));
        Assert.Equal(1, Assert.IsType<Literal>(skip.Count).Value);
    }

    [Fact]
    public void AnyAndFirstKeepTheirOptionalPredicate()
    {
        var any = Assert.IsType<Any>(
            Build("$source([1, 2]).$any(~ gt 1)"));
        Assert.NotNull(any.Predicate);

        var first = Assert.IsType<First>(
            Build("$source([1, 2]).$first()"));
        Assert.Null(first.Predicate);
    }

    [Fact]
    public void DistinctKeepsItsOptionalKey()
    {
        var withKey = Assert.IsType<Distinct>(
            Build("$source([{g: 'a'}, {g: 'b'}]).$distinct(g)"));
        Assert.Equal("g", Assert.IsType<Column>(withKey.Key!).Path);

        var withoutKey = Assert.IsType<Distinct>(
            Build("$source([1, 1]).$distinct()"));
        Assert.Null(withoutKey.Key);
    }

    [Fact]
    public void IndexIsAnExplicitExpression()
    {
        var plan = Build("$source(['a', 'b']).$map($index(1))");
        var project = Assert.IsType<Project>(plan);

        var index = Assert.IsType<IndexExpression>(
            Assert.Single(project.Projections).Expression);

        Assert.Equal(1, Assert.IsType<Literal>(index.Base).Value);
    }

    [Fact]
    public void LetValuesBecomeLiterals()
    {
        var plan = Build(
            "$let(@min, 1).$source([{id: 1}, {id: 2}]).$filter(id gt @min)");

        var filter = Assert.IsType<Filter>(plan);
        var binary = Assert.IsType<Binary>(filter.Predicate);

        Assert.Equal(1, Assert.IsType<Literal>(binary.Right).Value);
    }

    [Fact]
    public void RequestVariablesBecomeParameters()
    {
        var environment = QueryEnvironment.From(
            "min",
            new QueryParameter(SemanticTypeOptions.Int, 1));

        var plan = Build(
            "$source([{id: 1}]).$filter(id gt @min)",
            environment);

        var filter = Assert.IsType<Filter>(plan);
        var binary = Assert.IsType<Binary>(filter.Predicate);
        var parameter = Assert.IsType<Parameter>(binary.Right);

        Assert.Equal("min", parameter.Name);
        Assert.Equal("int", parameter.Type.Name);
    }

    [Fact]
    public void SourceVariableMustBeRegistered()
    {
        var environment = SourceEnvironment(OrderSchema);

        var error = Assert.Throws<QueryException>(
            () => Build("$source(@source).$count()", environment));

        Assert.Equal(QueryErrorCode.NotSupported, error.Code);
    }

    [Fact]
    public void FieldVariableResolvesToAColumn()
    {
        var plan = Build(
            "$let(@col, 'id').$source([{id: 1}]).$map($field(@col))");

        var project = Assert.IsType<Project>(plan);
        var column = Assert.IsType<Column>(
            Assert.Single(project.Projections).Expression);

        Assert.Equal("id", column.Path);
    }

    [Fact]
    public void NestedChainsBecomeSubqueries()
    {
        var plan = Build(
            "$source([1]).$map($source([1, 2]).$count())");

        var project = Assert.IsType<Project>(plan);
        var subquery = Assert.IsType<Subquery>(
            Assert.Single(project.Projections).Expression);

        Assert.IsType<Aggregate>(subquery.Plan);
    }

    [Fact]
    public void ScalarFunctionsBecomeCalls()
    {
        var plan = Build("$source(['A']).$map($lower(~))");

        var project = Assert.IsType<Project>(plan);
        var call = Assert.IsType<Call>(
            Assert.Single(project.Projections).Expression);

        Assert.Equal("$lower", call.Name);
    }

    [Fact]
    public void CoalesceAndDateBecomeCalls()
    {
        var coalesce = Assert.IsType<Call>(
            Assert.Single(
                Assert.IsType<Project>(
                    Build("$source([1]).$map($coalesce(null, ~))"))
                .Projections)
            .Expression);

        Assert.Equal("$coalesce", coalesce.Name);

        var date = Assert.IsType<Call>(
            Assert.Single(
                Assert.IsType<Project>(
                    Build("$source([{d: '2024-01-01'}]).$map($date(d))"))
                .Projections)
            .Expression);

        Assert.Equal("$date", date.Name);
    }

    [Fact]
    public void ObjectLiteralsInsideExpressionsBecomeObjectConstructs()
    {
        var plan = Build("$source([1]).$map({row: {value: ~}})");

        var project = Assert.IsType<Project>(plan);
        var projection = Assert.Single(project.Projections);

        Assert.Equal("row", projection.Key);

        var nested = Assert.IsType<ObjectConstruct>(projection.Expression);
        var field = Assert.Single(nested.Fields);

        Assert.Equal("value", field.Name);
        Assert.IsType<CurrentRow>(field.Value);
    }
}
