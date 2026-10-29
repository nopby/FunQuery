using FunQuery.Enums;
using FunQuery.PostgreSql;
using FunQuery.SemanticTypes;
using FunQuery.Sql;
using FunQuery.Sql.Sql;

namespace FunQuery.PostgreSql.Tests;

/// <summary>M6.2: golden SQL for the PostgreSQL dialect, without a database.</summary>
public class SqlRendererTests
{
    private static readonly ObjectType OrderSchema = Schema(
        ("id", SemanticTypeOptions.Int),
        ("customer", SemanticTypeOptions.String),
        ("total", SemanticTypeOptions.Decimal),
        ("active", SemanticTypeOptions.Boolean),
        ("orderedOn", SemanticTypeOptions.Date),
        ("orderedOnText", SemanticTypeOptions.String));

    private static ObjectType Schema(params (string Name, SemanticType Type)[] fields)
    {
        var map = new Dictionary<string, SemanticType>(StringComparer.Ordinal);

        foreach (var (name, type) in fields)
            map[name] = type;

        return new ObjectType(map);
    }

    private static QueryEnvironment SourceEnvironment() =>
        QueryEnvironment.From(
            "source",
            new QueryParameter(new ArrayType(OrderSchema), null));

    private static QueryEnvironment Environment(
        params (string Name, SemanticType Type, object? Value)[] variables)
    {
        var environment = SourceEnvironment();

        foreach (var (name, type, value) in variables)
            environment = environment.With(name, new QueryParameter(type, value));

        return environment;
    }

    private static SqlStatement Render(
        string query,
        QueryEnvironment? environment = null)
    {
        environment ??= SourceEnvironment();

        var ast = new QueryEngine().Prepare(query, environment).Expression;

        var sources = new Dictionary<string, PlanSource>(StringComparer.Ordinal)
        {
            ["source"] = new PlanSource("orders", OrderSchema),
        };

        var plan = PlanBuilder.Build(query, ast, sources, environment);

        var tables = new Dictionary<string, SqlTable>(StringComparer.Ordinal)
        {
            ["orders"] = new SqlTable("orders"),
        };

        return new SqlRenderer(PostgreSqlDialect.Instance, tables)
            .Render(plan, environment);
    }

    private static void AssertSql(
        string query,
        string expectedSql,
        params (string Name, object? Value)[] parameters)
    {
        var statement = Render(query);

        Assert.Equal(expectedSql, statement.Sql);
        Assert.Equal(parameters.Length, statement.Parameters.Count);

        for (int i = 0; i < parameters.Length; i++)
        {
            Assert.Equal(parameters[i].Name, statement.Parameters[i].Name);
            Assert.Equal(parameters[i].Value, statement.Parameters[i].Value);
        }
    }

    private static void AssertRejected(string query, QueryErrorCode code)
    {
        var error = Assert.Throws<QueryException>(() => Render(query));

        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void FilterUsesTwoValuedCoalesceAndAParameter()
    {
        AssertSql(
            "$source(@source).$filter(id gt 1)",
            "SELECT * FROM \"orders\" WHERE COALESCE((\"id\" > @p0), FALSE)",
            ("@p0", 1));
    }

    [Fact]
    public void ProjectionReplacesTheSelectList()
    {
        AssertSql(
            "$source(@source).$filter(id gt 1).$select(id, customer)",
            "SELECT \"id\", \"customer\" FROM \"orders\" " +
            "WHERE COALESCE((\"id\" > @p0), FALSE)",
            ("@p0", 1));
    }

    [Fact]
    public void SortAndTakeRenderNullsAndLimit()
    {
        AssertSql(
            "$source(@source).$sort(id, desc).$take(2)",
            "SELECT * FROM \"orders\" ORDER BY \"id\" DESC NULLS LAST LIMIT @p0",
            ("@p0", 2));
    }

    [Fact]
    public void SkipRendersOffset()
    {
        AssertSql(
            "$source(@source).$sort(id).$skip(1)",
            "SELECT * FROM \"orders\" ORDER BY \"id\" ASC NULLS FIRST OFFSET @p0",
            ("@p0", 1));
    }

    [Fact]
    public void CountWrapsTheInputInASubquery()
    {
        AssertSql(
            "$source(@source).$count()",
            "SELECT COUNT(*) FROM (SELECT * FROM \"orders\") AS \"t0\"");
    }

    [Fact]
    public void AnyRendersExists()
    {
        AssertSql(
            "$source(@source).$any(id gt 1)",
            "SELECT EXISTS (SELECT * FROM (SELECT * FROM \"orders\") AS \"t0\" " +
            "WHERE COALESCE((\"id\" > @p0), FALSE))",
            ("@p0", 1));
    }

    [Fact]
    public void FirstRendersLimitOne()
    {
        AssertSql(
            "$source(@source).$first(id gt 1)",
            "SELECT * FROM (SELECT * FROM \"orders\") AS \"t0\" " +
            "WHERE COALESCE((\"id\" > @p0), FALSE) LIMIT 1",
            ("@p0", 1));
    }

    [Fact]
    public void DistinctRendersWholeRowAndKeyedForms()
    {
        AssertSql(
            "$source(@source).$distinct()",
            "SELECT DISTINCT * FROM \"orders\"");

        AssertSql(
            "$source(@source).$distinct(id)",
            "SELECT DISTINCT ON (\"id\") * FROM (SELECT * FROM \"orders\") AS \"t0\" " +
            "ORDER BY \"id\" ASC NULLS FIRST");
    }

    [Fact]
    public void FilterAfterProjectionWrapsToResolveTheAlias()
    {
        AssertSql(
            "$source(@source).$select({x: id}).$filter(x gt 1)",
            "SELECT * FROM (SELECT \"id\" AS \"x\" FROM \"orders\") AS \"t0\" " +
            "WHERE COALESCE((\"x\" > @p0), FALSE)",
            ("@p0", 1));
    }

    [Fact]
    public void ProjectionAfterTakeWraps()
    {
        AssertSql(
            "$source(@source).$sort(id).$take(2).$select(id)",
            "SELECT \"id\" FROM (SELECT * FROM \"orders\" " +
            "ORDER BY \"id\" ASC NULLS FIRST LIMIT @p0) AS \"t0\"",
            ("@p0", 2));
    }

    [Fact]
    public void TakeAndSkipRequireAnExplicitSort()
    {
        AssertRejected(
            "$source(@source).$take(1)",
            QueryErrorCode.OrderRequired);

        AssertRejected(
            "$source(@source).$skip(1)",
            QueryErrorCode.OrderRequired);
    }

    [Fact]
    public void EqualityIsNullSafe()
    {
        AssertSql(
            "$source(@source).$filter(id eq null)",
            "SELECT * FROM \"orders\" WHERE ((\"id\") IS NOT DISTINCT FROM (NULL))");

        AssertSql(
            "$source(@source).$filter(id neq null)",
            "SELECT * FROM \"orders\" WHERE ((\"id\") IS DISTINCT FROM (NULL))");
    }

    [Fact]
    public void NotEqualAgainstAParameterIsNullSafe()
    {
        var environment = Environment(
            ("min", SemanticTypeOptions.Int, 1));

        var statement = Render(
            "$source(@source).$filter(id neq @min)",
            environment);

        Assert.Equal(
            "SELECT * FROM \"orders\" WHERE ((\"id\") IS DISTINCT FROM (@p0))",
            statement.Sql);

        Assert.Equal(1, statement.Parameters[0].Value);
    }

    [Fact]
    public void NotAndLogicalOperatorsStayTwoValued()
    {
        AssertSql(
            "$source(@source).$filter(not (id eq 1))",
            "SELECT * FROM \"orders\" WHERE (NOT ((\"id\") IS NOT DISTINCT FROM (@p0)))",
            ("@p0", 1));

        AssertSql(
            "$source(@source).$filter(id gt 1 and active)",
            "SELECT * FROM \"orders\" WHERE (COALESCE((\"id\" > @p0), FALSE) " +
            "AND COALESCE(\"active\", FALSE))",
            ("@p0", 1));
    }

    [Fact]
    public void TextOperatorsRenderWithBinaryCollation()
    {
        AssertSql(
            "$source(@source).$filter(customer contains 'a')",
            "SELECT * FROM \"orders\" WHERE COALESCE(strpos(\"customer\", @p0) > 0, FALSE)",
            ("@p0", "a"));

        AssertSql(
            "$source(@source).$filter(customer startswith 'a')",
            "SELECT * FROM \"orders\" WHERE COALESCE(strpos(\"customer\", @p0) = 1, FALSE)",
            ("@p0", "a"));

        AssertSql(
            "$source(@source).$filter(customer endswith 'a')",
            "SELECT * FROM \"orders\" WHERE COALESCE(" +
            "(right(\"customer\", length(@p0))) COLLATE \"C\" = " +
            "(@p0) COLLATE \"C\", FALSE)",
            ("@p0", "a"));
    }

    [Fact]
    public void StringEqualityCarriesBinaryCollation()
    {
        AssertSql(
            "$source(@source).$filter(customer eq 'a')",
            "SELECT * FROM \"orders\" WHERE " +
            "(((\"customer\") COLLATE \"C\") IS NOT DISTINCT FROM ((@p0) COLLATE \"C\"))",
            ("@p0", "a"));
    }

    [Fact]
    public void InListHandlesNullsAndEmptyLists()
    {
        AssertSql(
            "$source(@source).$filter(id in [1, 2])",
            "SELECT * FROM \"orders\" WHERE COALESCE((\"id\" IN (@p0, @p1)), FALSE)",
            ("@p0", 1),
            ("@p1", 2));

        AssertSql(
            "$source(@source).$filter(id in [1, null])",
            "SELECT * FROM \"orders\" WHERE COALESCE(" +
            "((\"id\" IN (@p0)) OR (\"id\" IS NULL)), FALSE)",
            ("@p0", 1));

        AssertSql(
            "$source(@source).$filter(id in [])",
            "SELECT * FROM \"orders\" WHERE COALESCE(FALSE, FALSE)");
    }

    [Fact]
    public void ScalarFunctionsRenderAsFunctions()
    {
        AssertSql(
            "$source(@source).$map(id)",
            "SELECT \"id\" FROM \"orders\"");

        AssertSql(
            "$source(@source).$map($lower(customer))",
            "SELECT lower((\"customer\") COLLATE \"C\") FROM \"orders\"");

        AssertSql(
            "$source(@source).$map($length(customer))",
            "SELECT length(\"customer\") FROM \"orders\"");

        AssertSql(
            "$source(@source).$map($coalesce(customer, 'x'))",
            "SELECT COALESCE(\"customer\", @p0) FROM \"orders\"",
            ("@p0", "x"));
    }

    [Fact]
    public void DateValuesBecomeTypedParameters()
    {
        AssertSql(
            "$source(@source).$map($date(orderedOnText))",
            "SELECT CAST(\"orderedOnText\" AS date) FROM \"orders\"");

        AssertSql(
            "$source(@source).$map($date('2024-01-15'))",
            "SELECT @p0 FROM \"orders\"",
            ("@p0", new DateOnly(2024, 1, 15)));
    }

    [Fact]
    public void SelectObjectFormUsesAliases()
    {
        AssertSql(
            "$source(@source).$select({label: customer})",
            "SELECT \"customer\" AS \"label\" FROM \"orders\"");
    }

    [Fact]
    public void FilterAfterTakeWrapsAndKeepsParameterOrder()
    {
        AssertSql(
            "$source(@source).$sort(id).$take(2).$filter(id gt 1)",
            "SELECT * FROM (SELECT * FROM \"orders\" " +
            "ORDER BY \"id\" ASC NULLS FIRST LIMIT @p0) AS \"t0\" " +
            "WHERE COALESCE((\"id\" > @p1), FALSE)",
            ("@p0", 2),
            ("@p1", 1));
    }

    [Fact]
    public void SortAfterTakeWraps()
    {
        AssertSql(
            "$source(@source).$sort(id).$take(2).$sort(id, desc)",
            "SELECT * FROM (SELECT * FROM \"orders\" " +
            "ORDER BY \"id\" ASC NULLS FIRST LIMIT @p0) AS \"t0\" " +
            "ORDER BY \"id\" DESC NULLS LAST",
            ("@p0", 2));
    }

    [Fact]
    public void UnsupportedFeaturesFailExplicitly()
    {
        AssertRejected(
            "$source([1, 2]).$count()",
            QueryErrorCode.NotSupported);

        AssertRejected(
            "$source(@source).$map($index())",
            QueryErrorCode.NotSupported);

        AssertRejected(
            "$source(@source).$select({row: ~})",
            QueryErrorCode.NotSupported);

        AssertRejected(
            "$source(@source).$map($lower('Ä'))",
            QueryErrorCode.NotSupported);

        AssertRejected(
            "$source(@source).$map($length('😀'))",
            QueryErrorCode.NotSupported);
    }
}
