using FunQuery.Enums;
using FunQuery.SemanticTypes;
using FunQuery.Sql;
using FunQuery.Sql.Plan;
using Npgsql;

namespace FunQuery.PostgreSql;

/// <summary>
/// Source PostgreSQL: metadata schema/tabel + eksekutor plan SQL. Query tetap dianalisis terhadap
/// schema oleh adapter sebelum executor dipanggil, sehingga field/tipe yang salah tidak menyentuh DB.
/// </summary>
public sealed class PostgreSqlSource : IQuerySourceExecutor, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly bool _ownsDataSource;
    private readonly int _commandTimeoutSeconds;

    public PostgreSqlSource(
        string name,
        ObjectType schema,
        string table,
        NpgsqlDataSource dataSource,
        TimeSpan? commandTimeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentException.ThrowIfNullOrEmpty(table);
        ArgumentNullException.ThrowIfNull(dataSource);

        Name = name;
        Schema = schema;
        Table = table;
        _dataSource = dataSource;
        _commandTimeoutSeconds = ToSeconds(
            commandTimeout ?? TimeSpan.FromSeconds(30));
    }

    /// <summary>Membuat source dengan data source sendiri; dispose source untuk menutupnya.</summary>
    public PostgreSqlSource(
        string name,
        ObjectType schema,
        string table,
        string connectionString,
        TimeSpan? commandTimeout = null)
        : this(
            name,
            schema,
            table,
            NpgsqlDataSource.Create(connectionString),
            commandTimeout)
    {
        _ownsDataSource = true;
    }

    public string Name { get; }

    public ObjectType Schema { get; }

    public string Table { get; }

    public ProviderCapabilities Capabilities =>
        PostgreSqlDialect.Instance.Capabilities;

    public async ValueTask<object?> ExecuteAsync(
        QueryPlan plan,
        QueryEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(environment);

        var sources = new Dictionary<string, PlanSource>(StringComparer.Ordinal)
        {
            ["source"] = new PlanSource(Name, Schema),
        };

        var relational = PlanBuilder.Build(
            plan.Query,
            plan.Expression,
            sources,
            environment);

        var tables = new Dictionary<string, SqlTable>(StringComparer.Ordinal)
        {
            [Name] = new SqlTable(Table),
        };

        var statement = new SqlRenderer(PostgreSqlDialect.Instance, tables)
            .Render(relational, environment);

        await using var command = _dataSource.CreateCommand(statement.Sql);
        command.CommandTimeout = Math.Min(
            _commandTimeoutSeconds,
            Math.Max(
                1,
                (int)Math.Ceiling(plan.Limits.MaxExecutionTime.TotalSeconds)));

        foreach (var parameter in statement.Parameters)
            command.Parameters.Add(PostgreSqlParameterBinder.Create(parameter));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var resultType = plan.Expression.SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "The analyzed query has no semantic type.");

        return await PostgreSqlRowReader.ReadAsync(
            reader,
            resultType,
            plan.Limits.MaxResultItems,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsDataSource)
            await _dataSource.DisposeAsync();
    }

    private static int ToSeconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "Command timeout must be positive.");
        }

        return Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
    }
}
