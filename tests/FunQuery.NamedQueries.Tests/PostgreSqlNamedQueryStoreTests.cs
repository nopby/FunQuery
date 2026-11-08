using Npgsql;

namespace FunQuery.NamedQueries.Tests;

public sealed class PostgreSqlNamedQueryStoreTests : IAsyncLifetime
{
    private const string TestSchema = "funquery_named_queries_test";
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("FUNQUERY_POSTGRES");

        if (string.IsNullOrWhiteSpace(_connectionString))
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            $"""
            DROP SCHEMA IF EXISTS "{TestSchema}" CASCADE;
            CREATE SCHEMA "{TestSchema}";
            CREATE TABLE "{TestSchema}"."named_queries" (
                app_id text NOT NULL DEFAULT 'global',
                tenant_id text NOT NULL DEFAULT 'global',
                query_key text NOT NULL,
                query_text text NOT NULL,
                source_name text NULL,
                enabled boolean NOT NULL DEFAULT true,
                cache_mode text NOT NULL DEFAULT 'default',
                l1_ttl_seconds integer NULL,
                l2_ttl_seconds integer NULL,
                version integer NOT NULL DEFAULT 1,
                parameters jsonb NULL,
                updated_at timestamptz NOT NULL DEFAULT now(),
                updated_by text NULL,
                PRIMARY KEY (app_id, tenant_id, query_key)
            );
            """,
            connection);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS \"{TestSchema}\" CASCADE",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task CrudAndVersioningWork()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        var options = new NamedQueryStoreOptions
        {
            ConnectionString = _connectionString,
            Schema = TestSchema,
        };

        await using var store = new PostgreSqlNamedQueryStore(options);
        var entry = new NamedQueryEntry
        {
            AppId = "app",
            TenantId = "tenant",
            Key = "orders",
            Query = "$source(@source).$count()",
            Source = "orders",
            UpdatedBy = "admin",
        };

        var created = await store.CreateAsync(entry, CancellationToken.None);
        Assert.Equal(1, created.Version);

        var loaded = await store.GetAsync("app", "tenant", "orders", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("$source(@source).$count()", loaded.Query);
        Assert.Equal("admin", loaded.UpdatedBy);

        var updated = await store.UpdateAsync(
            loaded with { Query = "$source(@source).$select(id)" },
            1,
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(2, updated.Version);

        var conflict = await store.UpdateAsync(
            loaded with { Query = "$source(@source).$select(total)" },
            1,
            CancellationToken.None);

        Assert.Null(conflict);

        var list = await store.ListAsync("app", "tenant", 10, 0, CancellationToken.None);
        Assert.Single(list);

        Assert.True(await store.DeleteAsync(
            "app", "tenant", "orders", 2, CancellationToken.None));
        Assert.False(await store.DeleteAsync(
            "app", "tenant", "orders", 2, CancellationToken.None));
    }
}
