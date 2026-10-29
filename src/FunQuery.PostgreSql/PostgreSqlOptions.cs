namespace FunQuery.PostgreSql;

public sealed class PostgreSqlOptions
{
    /// <summary>Connection string Npgsql, mis. <c>Host=localhost;Database=app;Username=...;Password=...</c>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Batas waktu satu command PostgreSQL. Default 30 detik.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ArgumentException(
                $"{nameof(ConnectionString)} must not be empty.",
                nameof(ConnectionString));
        }

        if (CommandTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CommandTimeout),
                CommandTimeout,
                $"{nameof(CommandTimeout)} must be positive.");
        }
    }
}
