namespace FunQuery.NamedQueries;

public sealed record NamedQueryWriteRequest
{
    public string? AppId { get; init; }

    public string? TenantId { get; init; }

    public string? Key { get; init; }

    public string? Query { get; init; }

    public string? Source { get; init; }

    public bool? Enabled { get; init; }

    public string? CacheMode { get; init; }

    public int? L1TtlSeconds { get; init; }

    public int? L2TtlSeconds { get; init; }

    public string? ParametersJson { get; init; }

    public int? Version { get; init; }
}

public sealed record NamedQueryResponse
{
    public required string AppId { get; init; }

    public required string TenantId { get; init; }

    public required string Key { get; init; }

    public required string Query { get; init; }

    public string? Source { get; init; }

    public bool Enabled { get; init; }

    public required string CacheMode { get; init; }

    public int? L1TtlSeconds { get; init; }

    public int? L2TtlSeconds { get; init; }

    public int Version { get; init; }

    public string? ParametersJson { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string? UpdatedBy { get; init; }

    public static NamedQueryResponse From(NamedQueryEntry entry) =>
        new()
        {
            AppId = entry.AppId,
            TenantId = entry.TenantId,
            Key = entry.Key,
            Query = entry.Query,
            Source = entry.Source,
            Enabled = entry.Enabled,
            CacheMode = entry.CacheMode switch
            {
                NamedQueryCacheMode.None => "none",
                NamedQueryCacheMode.Custom => "custom",
                _ => "default",
            },
            L1TtlSeconds = entry.L1TtlSeconds,
            L2TtlSeconds = entry.L2TtlSeconds,
            Version = entry.Version,
            ParametersJson = entry.ParametersJson,
            UpdatedAt = entry.UpdatedAt,
            UpdatedBy = entry.UpdatedBy,
        };
}

public sealed record NamedQueryListResponse(
    IReadOnlyList<NamedQueryResponse> Items);

public sealed record NamedQueryAdminError(
    string Code,
    string Message);
