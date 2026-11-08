using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FunQuery.NamedQueries;

/// <summary>
/// Mendengarkan <c>LISTEN/NOTIFY</c> dari tabel catalog dan menginvalidasi lookup cache lokal.
/// TTL tetap menjadi fallback bila listener terputus.
/// </summary>
public sealed class NamedQueryChangeListener : BackgroundService
{
    private readonly NamedQueryStoreOptions _options;
    private readonly INamedQueryCacheInvalidator _invalidator;
    private readonly ILogger<NamedQueryChangeListener> _logger;

    public NamedQueryChangeListener(
        NamedQueryStoreOptions options,
        INamedQueryCacheInvalidator invalidator,
        ILogger<NamedQueryChangeListener> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(invalidator);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _invalidator = invalidator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableChangeListener)
            return;

        var retryDelay = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(stoppingToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                _logger.LogWarning(
                    error,
                    "The named-query catalog listener stopped; retrying in {Delay}.",
                    retryDelay);

                await Task.Delay(retryDelay, stoppingToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(
                    Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = new NpgsqlCommand(
            $"LISTEN {Quote(_options.NotificationChannel)}",
            connection))
        {
            command.CommandTimeout = Math.Max(
                1,
                (int)Math.Ceiling(_options.CommandTimeout.TotalSeconds));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Listening for named-query changes on channel {Channel}.",
            _options.NotificationChannel);

        connection.Notification += (_, args) => Invalidate(args.Payload);

        while (!cancellationToken.IsCancellationRequested)
            await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Invalidate(string? payload)
    {
        if (payload is null)
            return;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("app_id", out var appId) &&
                root.TryGetProperty("tenant_id", out var tenantId) &&
                root.TryGetProperty("query_key", out var queryKey))
            {
                _invalidator.Invalidate(
                    appId.GetString() ?? "global",
                    tenantId.GetString() ?? "global",
                    queryKey.GetString() ?? string.Empty);
            }
        }
        catch (JsonException error)
        {
            _logger.LogWarning(
                error,
                "Ignoring malformed named-query notification payload.");
        }
    }

    private static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
