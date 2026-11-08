namespace FunQuery.Caching;

public enum QueryCacheMode
{
    Default,
    Disabled,
    Custom,
}

/// <summary>
/// Policy per request. Custom TTLs are clamped to the host maxima; they can shorten but never extend.
/// </summary>
public readonly record struct QueryCachePolicy(
    QueryCacheMode Mode,
    TimeSpan? LocalTtl = null,
    TimeSpan? DistributedTtl = null)
{
    public static QueryCachePolicy Default { get; } =
        new(QueryCacheMode.Default);

    public static QueryCachePolicy Disabled { get; } =
        new(QueryCacheMode.Disabled);
}
