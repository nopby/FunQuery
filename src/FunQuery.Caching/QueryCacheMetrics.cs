namespace FunQuery.Caching;

/// <summary>Counter cache untuk observability; aman dibaca bersamaan.</summary>
public sealed class QueryCacheMetrics
{
    private long _l1Hits;
    private long _l2Hits;
    private long _misses;
    private long _coalesced;
    private long _cacheErrors;
    private long _skippedEntries;

    public long L1Hits => Interlocked.Read(ref _l1Hits);

    public long L2Hits => Interlocked.Read(ref _l2Hits);

    public long Misses => Interlocked.Read(ref _misses);

    public long Coalesced => Interlocked.Read(ref _coalesced);

    public long CacheErrors => Interlocked.Read(ref _cacheErrors);

    public long SkippedEntries => Interlocked.Read(ref _skippedEntries);

    internal void RecordL1Hit() => Interlocked.Increment(ref _l1Hits);

    internal void RecordL2Hit() => Interlocked.Increment(ref _l2Hits);

    internal void RecordMiss() => Interlocked.Increment(ref _misses);

    internal void RecordCoalesced() => Interlocked.Increment(ref _coalesced);

    internal void RecordCacheError() => Interlocked.Increment(ref _cacheErrors);

    internal void RecordSkippedEntry() => Interlocked.Increment(ref _skippedEntries);
}
