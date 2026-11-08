using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FunQuery.Caching;

/// <summary>
/// Cache hasil query dengan L1 memory, L2 opsional, dan single-flight per key.
/// Payload selalu byte[] JSON, sehingga tidak ada serializer reflection.
/// </summary>
public sealed class QueryResultCache : IQueryResultCache
{
    private readonly IMemoryCache _l1;
    private readonly IDistributedCache? _l2;
    private readonly QueryCacheOptions _options;
    private readonly ILogger<QueryResultCache> _logger;
    private readonly QueryCacheMetrics _metrics;
    private readonly ConcurrentDictionary<string, Flight> _flights =
        new(StringComparer.Ordinal);

    public QueryResultCache(
        IMemoryCache l1,
        QueryCacheOptions options,
        IEnumerable<IDistributedCache> distributedCaches,
        ILogger<QueryResultCache>? logger = null,
        QueryCacheMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(l1);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(distributedCaches);

        _l1 = l1;
        _options = options;
        _l2 = distributedCaches.LastOrDefault();
        _logger = logger ?? NullLogger<QueryResultCache>.Instance;
        _metrics = metrics ?? new QueryCacheMetrics();
    }

    public bool Enabled => _options.Enabled;

    public QueryCacheMetrics Metrics => _metrics;

    public async ValueTask<byte[]?> TryGetAsync(
        string key,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (!Enabled)
            return null;

        if (_l1.TryGetValue(key, out byte[]? payload))
        {
            _metrics.RecordL1Hit();
            return payload;
        }

        return await TryGetL2Async(
            key,
            QueryCachePolicy.Default,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SetAsync(
        string key,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(payload);

        if (!Enabled)
            return;

        if (payload.Length > _options.MaxEntryBytes)
        {
            _metrics.RecordSkippedEntry();
            return;
        }

        SetL1(key, payload, QueryCachePolicy.Default);
        await SetL2Async(
            key,
            payload,
            QueryCachePolicy.Default,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<byte[]> GetOrCreateAsync(
        string key,
        Func<CancellationToken, ValueTask<byte[]>> factory,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(key, factory, QueryCachePolicy.Default, cancellationToken);

    public async ValueTask<byte[]> GetOrCreateAsync(
        string key,
        Func<CancellationToken, ValueTask<byte[]>> factory,
        QueryCachePolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (!Enabled || policy.Mode == QueryCacheMode.Disabled)
            return await factory(cancellationToken).ConfigureAwait(false);

        if (_l1.TryGetValue(key, out byte[]? cached))
        {
            _metrics.RecordL1Hit();
            return cached!;
        }

        var fromL2 = await TryGetL2Async(key, policy, cancellationToken)
            .ConfigureAwait(false);

        if (fromL2 is not null)
            return fromL2;

        _metrics.RecordMiss();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_l1.TryGetValue(key, out cached))
            {
                _metrics.RecordL1Hit();
                return cached!;
            }

            if (!_flights.TryGetValue(key, out var flight))
            {
                var candidate = new Flight(factory, policy);

                if (_flights.TryAdd(key, candidate))
                {
                    StartLeader(key, candidate);
                    flight = candidate;
                }
                else
                {
                    continue;
                }
            }
            else
            {
                _metrics.RecordCoalesced();
            }

            Interlocked.Increment(ref flight.Waiters);

            try
            {
                return await flight.Completion.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The shared flight was cancelled because every waiter left. Retry
                // with a fresh flight rather than surfacing someone else's cancellation.
                continue;
            }
            finally
            {
                if (Interlocked.Decrement(ref flight.Waiters) == 0 &&
                    !flight.Completion.Task.IsCompleted)
                {
                    flight.Cts.Cancel();
                }
            }
        }
    }

    private void StartLeader(string key, Flight flight) =>
        _ = RunLeaderAsync(key, flight);

    private async Task RunLeaderAsync(string key, Flight flight)
    {
        try
        {
            var cached = await TryGetL2Async(
                key,
                flight.Policy,
                flight.Cts.Token).ConfigureAwait(false);

            byte[] result;

            if (cached is not null)
            {
                result = cached;
            }
            else
            {
                result = await flight.Factory(flight.Cts.Token)
                    .ConfigureAwait(false);

                if (result.Length > _options.MaxEntryBytes)
                {
                    _metrics.RecordSkippedEntry();
                }
                else
                {
                    await SetL2Async(key, result, flight.Policy, flight.Cts.Token)
                        .ConfigureAwait(false);
                    SetL1(key, result, flight.Policy);
                }
            }

            flight.Completion.TrySetResult(result);
        }
        catch (OperationCanceledException) when (flight.Cts.IsCancellationRequested)
        {
            flight.Completion.TrySetCanceled(flight.Cts.Token);
        }
        catch (Exception error)
        {
            _metrics.RecordCacheError();
            _logger.LogWarning(
                error,
                "The query execution shared by the cache failed.");
            flight.Completion.TrySetException(error);
        }
        finally
        {
            ((ICollection<KeyValuePair<string, Flight>>)_flights).Remove(
                new KeyValuePair<string, Flight>(key, flight));
        }
    }

    private async ValueTask<byte[]?> TryGetL2Async(
        string key,
        QueryCachePolicy policy,
        CancellationToken cancellationToken)
    {
        if (_l2 is null)
            return null;

        try
        {
            var payload = await _l2.GetAsync(key, cancellationToken)
                .ConfigureAwait(false);

            if (payload is null)
                return null;

            if (payload.Length > _options.MaxEntryBytes)
            {
                _metrics.RecordSkippedEntry();
                return null;
            }

            _metrics.RecordL2Hit();
            SetL1(key, payload, policy);
            return payload;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            _metrics.RecordCacheError();
            _logger.LogWarning(
                error,
                "The distributed query cache read failed; treating it as a miss.");
            return null;
        }
    }

    private async ValueTask SetL2Async(
        string key,
        byte[] payload,
        QueryCachePolicy policy,
        CancellationToken cancellationToken)
    {
        if (_l2 is null)
            return;

        try
        {
            await _l2.SetAsync(
                key,
                payload,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = EffectiveTtls(policy).Distributed,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            _metrics.RecordCacheError();
            _logger.LogWarning(
                error,
                "The distributed query cache write failed; the result is still returned.");
        }
    }

    private void SetL1(
        string key,
        byte[] payload,
        QueryCachePolicy policy)
    {
        try
        {
            _l1.Set(
                key,
                payload,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = EffectiveTtls(policy).Local,
                    Size = 1,
                });
        }
        catch (Exception error)
        {
            _metrics.RecordCacheError();
            _logger.LogWarning(
                error,
                "The local query cache write failed; the result is still returned.");
        }
    }

    private (TimeSpan Local, TimeSpan Distributed) EffectiveTtls(
        QueryCachePolicy policy)
    {
        var local = policy.Mode == QueryCacheMode.Custom &&
                    policy.LocalTtl is { } requestedLocal
            ? requestedLocal
            : _options.LocalTtl;

        var distributed = policy.Mode == QueryCacheMode.Custom &&
                          policy.DistributedTtl is { } requestedDistributed
            ? requestedDistributed
            : _options.DistributedTtl;

        if (local > _options.LocalTtl)
            local = _options.LocalTtl;

        if (distributed > _options.DistributedTtl)
            distributed = _options.DistributedTtl;

        if (local > distributed)
            local = distributed;

        return (local, distributed);
    }

    private sealed class Flight
    {
        public Flight(
            Func<CancellationToken, ValueTask<byte[]>> factory,
            QueryCachePolicy policy)
        {
            Factory = factory;
            Policy = policy;
            Completion = new TaskCompletionSource<byte[]>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Func<CancellationToken, ValueTask<byte[]>> Factory { get; }

        public QueryCachePolicy Policy { get; }

        public TaskCompletionSource<byte[]> Completion { get; }

        public CancellationTokenSource Cts { get; } = new();

        public int Waiters;
    }
}
