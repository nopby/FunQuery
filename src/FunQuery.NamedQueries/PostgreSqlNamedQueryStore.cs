using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace FunQuery.NamedQueries;

public sealed class PostgreSqlNamedQueryStore : INamedQueryStore, IAsyncDisposable
{
    private readonly NamedQueryStoreOptions _options;
    private readonly NpgsqlDataSource _dataSource;

    public PostgreSqlNamedQueryStore(NamedQueryStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _dataSource = NpgsqlDataSource.Create(options.ConnectionString);
    }

    public async ValueTask<NamedQueryEntry?> GetAsync(
        string appId,
        string tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        ValidateScope(appId, tenantId, key);

        await using var command = CreateCommand(
            $"""
            SELECT app_id, tenant_id, query_key, query_text, source_name, enabled,
                   cache_mode, l1_ttl_seconds, l2_ttl_seconds, version, parameters,
                   updated_at, updated_by
            FROM {Table}
            WHERE app_id = @app_id AND tenant_id = @tenant_id AND query_key = @query_key
            """);

        command.Parameters.AddWithValue("app_id", appId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("query_key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadEntry(reader)
            : null;
    }

    public async ValueTask<IReadOnlyList<NamedQueryEntry>> ListAsync(
        string appId,
        string tenantId,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        ValidateScope(appId, tenantId);
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(offset, 0);

        await using var command = CreateCommand(
            $"""
            SELECT app_id, tenant_id, query_key, query_text, source_name, enabled,
                   cache_mode, l1_ttl_seconds, l2_ttl_seconds, version, parameters,
                   updated_at, updated_by
            FROM {Table}
            WHERE app_id = @app_id AND tenant_id = @tenant_id
            ORDER BY query_key
            LIMIT @limit OFFSET @offset
            """);

        command.Parameters.AddWithValue("app_id", appId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        var result = new List<NamedQueryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadEntry(reader));

        return result;
    }

    public async ValueTask<NamedQueryEntry> CreateAsync(
        NamedQueryEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Validate();

        await using var command = CreateCommand(
            $"""
            INSERT INTO {Table}
                (app_id, tenant_id, query_key, query_text, source_name, enabled,
                 cache_mode, l1_ttl_seconds, l2_ttl_seconds, version, parameters,
                 updated_at, updated_by)
            VALUES
                (@app_id, @tenant_id, @query_key, @query_text, @source_name, @enabled,
                 @cache_mode, @l1_ttl_seconds, @l2_ttl_seconds, 1, @parameters,
                 now(), @updated_by)
            RETURNING version, updated_at
            """);

        AddEntryParameters(command, entry);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);

            return entry with
            {
                Version = reader.GetInt32(0),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(1),
            };
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new NamedQueryAlreadyExistsException(
                $"Named query '{entry.Key}' already exists for " +
                $"app '{entry.AppId}' tenant '{entry.TenantId}'.");
        }
    }

    public async ValueTask<NamedQueryEntry?> UpdateAsync(
        NamedQueryEntry entry,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Validate();

        if (expectedVersion < 1)
            throw new NamedQueryValidationException("expected version must be at least 1.");

        await using var command = CreateCommand(
            $"""
            UPDATE {Table}
            SET query_text = @query_text,
                source_name = @source_name,
                enabled = @enabled,
                cache_mode = @cache_mode,
                l1_ttl_seconds = @l1_ttl_seconds,
                l2_ttl_seconds = @l2_ttl_seconds,
                parameters = @parameters,
                version = version + 1,
                updated_at = now(),
                updated_by = @updated_by
            WHERE app_id = @app_id
              AND tenant_id = @tenant_id
              AND query_key = @query_key
              AND version = @expected_version
            RETURNING version, updated_at
            """);

        AddEntryParameters(command, entry);
        command.Parameters.AddWithValue("expected_version", expectedVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return entry with
        {
            Version = reader.GetInt32(0),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(1),
        };
    }

    public async ValueTask<bool> DeleteAsync(
        string appId,
        string tenantId,
        string key,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        ValidateScope(appId, tenantId, key);

        await using var command = CreateCommand(
            $"""
            DELETE FROM {Table}
            WHERE app_id = @app_id
              AND tenant_id = @tenant_id
              AND query_key = @query_key
              AND version = @expected_version
            """);

        command.Parameters.AddWithValue("app_id", appId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("query_key", key);
        command.Parameters.AddWithValue("expected_version", expectedVersion);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private string Table =>
        $"{Quote(_options.Schema)}.{Quote(_options.Table)}";

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(_options.CommandTimeout.TotalSeconds));
        return command;
    }

    private static void AddEntryParameters(NpgsqlCommand command, NamedQueryEntry entry)
    {
        command.Parameters.AddWithValue("app_id", entry.AppId);
        command.Parameters.AddWithValue("tenant_id", entry.TenantId);
        command.Parameters.AddWithValue("query_key", entry.Key);
        command.Parameters.AddWithValue("query_text", entry.Query);
        command.Parameters.AddWithValue(
            "source_name",
            (object?)entry.Source ?? DBNull.Value);
        command.Parameters.AddWithValue("enabled", entry.Enabled);
        command.Parameters.AddWithValue("cache_mode", ModeText(entry.CacheMode));
        command.Parameters.AddWithValue(
            "l1_ttl_seconds",
            (object?)entry.L1TtlSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "l2_ttl_seconds",
            (object?)entry.L2TtlSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "parameters",
            NpgsqlDbType.Jsonb,
            (object?)entry.ParametersJson ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "updated_by",
            (object?)entry.UpdatedBy ?? DBNull.Value);
    }

    private static NamedQueryEntry ReadEntry(NpgsqlDataReader reader) =>
        new()
        {
            AppId = reader.GetString(0),
            TenantId = reader.GetString(1),
            Key = reader.GetString(2),
            Query = reader.GetString(3),
            Source = reader.IsDBNull(4) ? null : reader.GetString(4),
            Enabled = reader.GetBoolean(5),
            CacheMode = ParseMode(reader.GetString(6)),
            L1TtlSeconds = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            L2TtlSeconds = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            Version = reader.GetInt32(9),
            ParametersJson = reader.IsDBNull(10) ? null : reader.GetString(10),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(11),
            UpdatedBy = reader.IsDBNull(12) ? null : reader.GetString(12),
        };

    private static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string ModeText(NamedQueryCacheMode mode) =>
        mode switch
        {
            NamedQueryCacheMode.None => "none",
            NamedQueryCacheMode.Default => "default",
            NamedQueryCacheMode.Custom => "custom",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    private static NamedQueryCacheMode ParseMode(string value) =>
        value switch
        {
            "none" => NamedQueryCacheMode.None,
            "default" => NamedQueryCacheMode.Default,
            "custom" => NamedQueryCacheMode.Custom,
            _ => throw new InvalidOperationException(
                $"Unknown named query cache_mode '{value}'."),
        };

    private static void ValidateScope(
        string appId,
        string tenantId,
        string? key = null)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new NamedQueryValidationException("app_id must not be empty.");

        if (string.IsNullOrWhiteSpace(tenantId))
            throw new NamedQueryValidationException("tenant_id must not be empty.");

        if (key is not null &&
            (string.IsNullOrWhiteSpace(key) || key.Length > NamedQueryEntry.MaxKeyLength))
        {
            throw new NamedQueryValidationException("query_key is invalid.");
        }
    }
}
