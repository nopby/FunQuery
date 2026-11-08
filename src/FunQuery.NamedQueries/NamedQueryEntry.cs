using System.Text.RegularExpressions;

namespace FunQuery.NamedQueries;

public enum NamedQueryCacheMode
{
    None,
    Default,
    Custom,
}

/// <summary>Satu named query yang disimpan di catalog.</summary>
public sealed record NamedQueryEntry
{
    public const int MaxKeyLength = 128;
    public const int MaxQueryLength = 16 * 1024;

    private static readonly Regex KeyPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public required string AppId { get; init; }

    public required string TenantId { get; init; }

    public required string Key { get; init; }

    public required string Query { get; init; }

    public string? Source { get; init; }

    public bool Enabled { get; init; } = true;

    public NamedQueryCacheMode CacheMode { get; init; } = NamedQueryCacheMode.Default;

    public int? L1TtlSeconds { get; init; }

    public int? L2TtlSeconds { get; init; }

    public int Version { get; init; } = 1;

    /// <summary>Reserved for declared parameter metadata.</summary>
    public string? ParametersJson { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? UpdatedBy { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AppId))
            throw new NamedQueryValidationException("app_id must not be empty.");

        if (string.IsNullOrWhiteSpace(TenantId))
            throw new NamedQueryValidationException("tenant_id must not be empty.");

        if (string.IsNullOrWhiteSpace(Key) || Key.Length > MaxKeyLength ||
            !KeyPattern.IsMatch(Key))
        {
            throw new NamedQueryValidationException(
                "query_key must match [A-Za-z0-9][A-Za-z0-9._-]{0,127}.");
        }

        if (string.IsNullOrWhiteSpace(Query))
            throw new NamedQueryValidationException("query_text must not be empty.");

        if (Query.Length > MaxQueryLength)
            throw new NamedQueryValidationException(
                $"query_text must be at most {MaxQueryLength} characters.");

        if (CacheMode == NamedQueryCacheMode.Custom)
        {
            if (L1TtlSeconds is null or <= 0)
                throw new NamedQueryValidationException(
                    "l1_ttl_seconds must be positive for cache_mode=custom.");

            if (L2TtlSeconds is null or <= 0)
                throw new NamedQueryValidationException(
                    "l2_ttl_seconds must be positive for cache_mode=custom.");

            if (L1TtlSeconds > L2TtlSeconds)
                throw new NamedQueryValidationException(
                    "l1_ttl_seconds must not exceed l2_ttl_seconds.");
        }

        if (Version < 1)
            throw new NamedQueryValidationException("version must be at least 1.");
    }
}

public sealed class NamedQueryValidationException(
    string message,
    bool isQueryError = false) : Exception(message)
{
    public bool IsQueryError { get; } = isQueryError;
}

public sealed class NamedQueryAlreadyExistsException(string message) : Exception(message);

public sealed class NamedQueryVersionConflictException(string message) : Exception(message);

public sealed class NamedQueryNotFoundException(string message) : Exception(message);
