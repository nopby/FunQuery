using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FunQuery.NamedQueries.Tests;

public sealed class NamedQueryChangeListenerTests : IAsyncLifetime
{
    private const string TestSchema = "funquery_nq_listener_test";
    private string? _connectionString;
    private string _channel = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("FUNQUERY_POSTGRES");

        if (string.IsNullOrWhiteSpace(_connectionString))
            return;

        _channel = "nq_test_" + Guid.NewGuid().ToString("N")[..24];

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

            CREATE FUNCTION "{TestSchema}".notify()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM pg_notify(
                    '{_channel}',
                    json_build_object(
                        'app_id', COALESCE(NEW.app_id, OLD.app_id),
                        'tenant_id', COALESCE(NEW.tenant_id, OLD.tenant_id),
                        'query_key', COALESCE(NEW.query_key, OLD.query_key)
                    )::text);
                RETURN COALESCE(NEW, OLD);
            END;
            $$;

            CREATE TRIGGER notify
            AFTER INSERT OR UPDATE OR DELETE ON "{TestSchema}"."named_queries"
            FOR EACH ROW EXECUTE FUNCTION "{TestSchema}".notify();
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
    public async Task NotificationInvalidatesTheLookupCache()
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;

        var options = new NamedQueryStoreOptions
        {
            ConnectionString = _connectionString,
            Schema = TestSchema,
            NotificationChannel = _channel,
        };

        var invalidator = new TestInvalidator();
        using var listener = new NamedQueryChangeListener(
            options,
            invalidator,
            NullLogger<NamedQueryChangeListener>.Instance);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await listener.StartAsync(stop.Token);

        // Give LISTEN a moment to be established before inserting.
        await Task.Delay(500, stop.Token);

        await using var store = new PostgreSqlNamedQueryStore(options);
        await store.CreateAsync(
            new NamedQueryEntry
            {
                AppId = "app",
                TenantId = "tenant",
                Key = "orders",
                Query = "$source(@source).$count()",
                Source = "orders",
                UpdatedBy = "admin",
            },
            stop.Token);

        var invalidated = await invalidator.Invalidated.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(("app", "tenant", "orders"), invalidated);

        await listener.StopAsync(CancellationToken.None);
    }

    private sealed class TestInvalidator : INamedQueryCacheInvalidator
    {
        public TaskCompletionSource<(string App, string Tenant, string Key)> Invalidated
        {
            get;
        } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Invalidate(string appId, string tenantId, string key) =>
            Invalidated.TrySetResult((appId, tenantId, key));
    }
}
