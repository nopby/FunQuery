namespace FunQuery.NamedQueries;

public sealed class NamedQueryStoreOptions
{
    /// <summary>Connection string to the database that owns the named-query catalog.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>PostgreSQL schema, default <c>funquery</c>.</summary>
    public string Schema { get; set; } = "funquery";

    /// <summary>Catalog table, default <c>named_queries</c>.</summary>
    public string Table { get; set; } = "named_queries";

    /// <summary>Channel used by the catalog LISTEN/NOTIFY trigger.</summary>
    public string NotificationChannel { get; set; } = "funquery_named_queries";

    /// <summary>Short lookup-cache lifetime; NOTIFY is the primary invalidation path.</summary>
    public TimeSpan LookupCacheTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Enable the PostgreSQL LISTEN/NOTIFY background listener.</summary>
    public bool EnableChangeListener { get; set; } = true;

    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new ArgumentException(
                $"{nameof(ConnectionString)} must not be empty.",
                nameof(ConnectionString));

        ValidateIdentifier(Schema, nameof(Schema));
        ValidateIdentifier(Table, nameof(Table));

        if (string.IsNullOrWhiteSpace(NotificationChannel))
            throw new ArgumentException(
                $"{nameof(NotificationChannel)} must not be empty.",
                nameof(NotificationChannel));

        if (LookupCacheTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(LookupCacheTtl),
                LookupCacheTtl,
                $"{nameof(LookupCacheTtl)} must be positive.");

        if (CommandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(CommandTimeout),
                CommandTimeout,
                $"{nameof(CommandTimeout)} must be positive.");
    }

    private static void ValidateIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 63)
            throw new ArgumentException(
                $"{name} must be 1-63 characters.",
                name);
    }
}
