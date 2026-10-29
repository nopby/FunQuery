using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.SemanticTypes;
using Npgsql;

namespace FunQuery.PostgreSql.Tests;

/// <summary>
/// M6.3: live PostgreSQL integration. Set FUNQUERY_POSTGRES to run; tests return early when it is
/// absent so the suite still runs on machines without a database.
/// </summary>
public sealed class PostgreSqlIntegrationTests : IAsyncLifetime
{
    private const string TableName = "funquery_m63_orders";

    private static readonly ObjectType Schema = new(
        new Dictionary<string, SemanticType>(StringComparer.Ordinal)
        {
            ["id"] = SemanticTypeOptions.Int,
            ["customer"] = SemanticTypeOptions.String,
            ["total"] = SemanticTypeOptions.Decimal,
            ["ordered_on"] = SemanticTypeOptions.Date,
            ["nickname"] = SemanticTypeOptions.String,
            ["active"] = SemanticTypeOptions.Boolean,
        });

    private static readonly IReadOnlyDictionary<string, object?>[] DataRows =
    [
        Row(1, "Ana", 10.5m, new DateOnly(2024, 1, 15), "ana", true),
        Row(2, "Budi", 5m, new DateOnly(2024, 2, 3), null, false),
        Row(3, "Citra", 20m, new DateOnly(2024, 3, 21), "ci", true),
        Row(4, "O'Reilly\n", 7m, new DateOnly(2024, 4, 1), null, null),
    ];

    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("FUNQUERY_POSTGRES");

        if (string.IsNullOrWhiteSpace(_connectionString))
            return;

        _dataSource = NpgsqlDataSource.Create(_connectionString);

        await using (var command = _dataSource.CreateCommand($"""
            DROP TABLE IF EXISTS "{TableName}";
            CREATE TABLE "{TableName}" (
                "id" integer PRIMARY KEY,
                "customer" text NOT NULL,
                "total" numeric NOT NULL,
                "ordered_on" date NOT NULL,
                "nickname" text,
                "active" boolean
            );
            INSERT INTO "{TableName}"
                ("id", "customer", "total", "ordered_on", "nickname", "active") VALUES
                (1, 'Ana', 10.5, DATE '2024-01-15', 'ana', TRUE),
                (2, 'Budi', 5, DATE '2024-02-03', NULL, FALSE),
                (3, 'Citra', 20, DATE '2024-03-21', 'ci', TRUE);
            """))
        {
            await command.ExecuteNonQueryAsync();
        }

        await using (var insert = _dataSource.CreateCommand($"""
            INSERT INTO "{TableName}"
                ("id", "customer", "total", "ordered_on", "nickname", "active")
            VALUES (@id, @customer, @total, @ordered_on, @nickname, @active)
            """))
        {
            insert.Parameters.AddWithValue("id", 4);
            insert.Parameters.AddWithValue("customer", "O'Reilly\n");
            insert.Parameters.AddWithValue("total", 7m);
            insert.Parameters.AddWithValue("ordered_on", new DateOnly(2024, 4, 1));
            insert.Parameters.AddWithValue("nickname", DBNull.Value);
            insert.Parameters.AddWithValue("active", DBNull.Value);

            await insert.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is null)
            return;

        await using (var command = _dataSource.CreateCommand(
            $"DROP TABLE IF EXISTS \"{TableName}\""))
        {
            await command.ExecuteNonQueryAsync();
        }

        await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task FilterAndSelectReturnTheExpectedRows()
    {
        if (_dataSource is null) return;

        var result = await ExecuteAsync(
            "$source(@source).$filter(total gt 10).$select(id, customer)");

        Assert.Equal(
            "[{\"id\":1,\"customer\":\"Ana\"},{\"id\":3,\"customer\":\"Citra\"}]",
            ValueJson.Serialize(result));
    }

    [Fact]
    public async Task ParametersWithQuotesAndNewlinesAreSafe()
    {
        if (_dataSource is null) return;

        var environment = EnvironmentWith(
            ("name", SemanticTypeOptions.String, "O'Reilly\n"));

        var result = await ExecuteAsync(
            "$source(@source).$filter(customer eq @name).$select(id)",
            environment);

        Assert.Equal("[{\"id\":4}]", ValueJson.Serialize(result));
    }

    [Fact]
    public async Task CountAnyFirstAndDateComparisonWork()
    {
        if (_dataSource is null) return;

        Assert.Equal(
            "4",
            ValueJson.Serialize(await ExecuteAsync("$source(@source).$count()")));

        Assert.Equal(
            "false",
            ValueJson.Serialize(await ExecuteAsync(
                "$source(@source).$any(total gt 100)")));

        var first = await ExecuteAsync(
            "$source(@source).$sort(id).$first(id gt 2)");

        Assert.Equal(
            "{\"id\":3,\"customer\":\"Citra\",\"total\":20,\"ordered_on\":\"2024-03-21\"," +
            "\"nickname\":\"ci\",\"active\":true}",
            ValueJson.Serialize(first));

        var dates = await ExecuteAsync(
            "$source(@source).$filter(ordered_on gt $date('2024-02-01'))" +
            ".$select(id)");

        Assert.Equal(
            "[{\"id\":2},{\"id\":3},{\"id\":4}]",
            ValueJson.Serialize(dates));
    }

    [Fact]
    public async Task SkipAndTakeWorkAfterSort()
    {
        if (_dataSource is null) return;

        var result = await ExecuteAsync(
            "$source(@source).$sort(id).$skip(1).$take(2).$select(id)");

        Assert.Equal("[{\"id\":2},{\"id\":3}]", ValueJson.Serialize(result));
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        if (_dataSource is null) return;

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var environment = EnvironmentWith();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExecuteAsync(
                "$source(@source).$count()",
                environment,
                cancellation.Token));

        Assert.NotNull(error);
    }

    [Fact]
    public void UnknownFieldsFailBeforeExecution()
    {
        var environment = EnvironmentWith();

        var error = Assert.Throws<QueryException>(() =>
            new QueryEngine().Prepare(
                "$source(@source).$filter(nope eq 1)",
                environment));

        Assert.Equal(QueryErrorCode.UnknownIdentifier, error.Code);
    }

    [Fact]
    public async Task PaginationWithoutSortFailsBeforeExecution()
    {
        if (_dataSource is null) return;

        var environment = EnvironmentWith();

        var plan = new QueryEngine().Prepare(
            "$source(@source).$take(1)",
            environment);

        await using var source = new PostgreSqlSource(
            "orders",
            Schema,
            TableName,
            _dataSource);

        var error = await Assert.ThrowsAsync<QueryException>(
            () => source.ExecuteAsync(plan, environment).AsTask());

        Assert.Equal(QueryErrorCode.OrderRequired, error.Code);
    }

    [Fact]
    public async Task ParityFilterProjectSortPaginationAndAggregates()
    {
        if (_dataSource is null) return;

        await AssertParityAsync(
            "$source(@source).$filter(total gt 10).$select(id, customer)");

        await AssertParityAsync(
            "$source(@source).$sort(id, desc).$take(2).$select(id)");

        await AssertParityAsync(
            "$source(@source).$sort(id).$skip(1).$take(2).$select(id)");

        await AssertParityAsync("$source(@source).$count()");
        await AssertParityAsync("$source(@source).$any(active)");
        await AssertParityAsync("$source(@source).$sort(id).$first(total gt 10)");

        await AssertParityAsync(
            "$source(@source).$sort(id).$map($index())");

        await AssertParityAsync(
            "$source(@source).$sort(id).$filter($index() lt 2).$select(id)");

        await AssertParityAsync(
            "$source(@source).$sort(id).$filter($index(1) lt 3).$select(id)");
    }

    [Fact]
    public async Task ParityStringsDatesNullsAndCoalesce()
    {
        if (_dataSource is null) return;

        await AssertParityAsync(
            "$source(@source).$filter(customer contains 'a').$select(id)");

        await AssertParityAsync(
            "$source(@source).$filter(customer startswith 'A').$select(id)");

        await AssertParityAsync(
            "$source(@source).$filter(customer endswith 'a').$select(id)");

        await AssertParityAsync(
            "$source(@source).$filter($lower(customer) eq 'ana').$select(id)");

        await AssertParityAsync(
            "$source(@source).$filter(ordered_on gt $date('2024-02-01')).$select(id)");

        await AssertParityAsync(
            "$source(@source).$filter(nickname eq null).$select(id)");

        await AssertParityAsync(
            "$source(@source).$filter(nickname neq null).$select(id)");

        await AssertParityAsync(
            "$source(@source).$filter(not active).$select(id)");

        await AssertParityAsync(
            "$source(@source).$filter(active or total gt 15).$select(id)");

        await AssertParityAsync(
            "$source(@source).$select({name: $coalesce(nickname, 'none')})" +
            ".$sort(name).$select(name)");

        await AssertParityAsync(
            "$source(@source).$distinct(nickname).$count()");
    }

    private async Task AssertParityAsync(
        string query,
        params (string Name, SemanticType Type, object? Value)[] variables)
    {
        var environment = EnvironmentWith(variables);
        var plan = new QueryEngine().Prepare(query, environment);

        var inMemory = new QueryEngine()
            .ExecutePlan(plan, environment)
            .ToJson();

        await using var source = new PostgreSqlSource(
            "orders",
            Schema,
            TableName,
            _dataSource!);

        var sql = ValueJson.Serialize(
            await source.ExecuteAsync(plan, environment));

        Assert.Equal(inMemory, sql);
    }

    private async Task<object?> ExecuteAsync(
        string query,
        QueryEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        environment ??= EnvironmentWith();

        var plan = new QueryEngine().Prepare(query, environment);

        await using var source = new PostgreSqlSource(
            "orders",
            Schema,
            TableName,
            _dataSource!);

        return await source.ExecuteAsync(plan, environment, cancellationToken);
    }

    private static QueryEnvironment EnvironmentWith(
        params (string Name, SemanticType Type, object? Value)[] variables)
    {
        var rows = SourceRows.Materialize("orders", Schema, DataRows);

        var environment = QueryEnvironment.From(
            "source",
            new QueryParameter(new ArrayType(Schema), rows));

        foreach (var (name, type, value) in variables)
            environment = environment.With(name, new QueryParameter(type, value));

        return environment;
    }

    private static IReadOnlyDictionary<string, object?> Row(
        int id,
        string customer,
        decimal total,
        DateOnly orderedOn,
        string? nickname,
        bool? active) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["customer"] = customer,
            ["total"] = total,
            ["ordered_on"] = orderedOn,
            ["nickname"] = nickname,
            ["active"] = active,
        };
}
