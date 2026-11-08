using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FunQuery.Caching.Tests;

public sealed class QueryResultCacheTests
{
    private static QueryCacheOptions Options(
        bool enabled = true,
        int maxEntryBytes = 1024) => new()
    {
        Enabled = enabled,
        LocalTtl = TimeSpan.FromMinutes(1),
        DistributedTtl = TimeSpan.FromMinutes(2),
        MaxEntryBytes = maxEntryBytes,
        MaxEntries = 100,
    };

    private static QueryResultCache Create(
        IDistributedCache? l2 = null,
        QueryCacheOptions? options = null,
        QueryCacheMetrics? metrics = null) =>
        new(
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            options ?? Options(),
            l2 is null ? [] : [l2],
            NullLogger<QueryResultCache>.Instance,
            metrics);

    [Fact]
    public async Task L1HitDoesNotCallTheFactoryAgain()
    {
        var cache = Create();
        var calls = 0;
        var payload = new byte[] { 1, 2, 3 };

        ValueTask<byte[]> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(payload);
        }

        var first = await cache.GetOrCreateAsync("k", Factory, CancellationToken.None);
        var second = await cache.GetOrCreateAsync("k", Factory, CancellationToken.None);

        Assert.Equal(payload, first);
        Assert.Equal(payload, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task L2HitFillsL1AndSkipsTheFactory()
    {
        var l2 = new FakeDistributedCache();
        var first = Create(l2);
        var calls = 0;

        await first.GetOrCreateAsync(
            "k",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(new byte[] { 7 });
            },
            CancellationToken.None);

        var second = Create(l2);
        var result = await second.GetOrCreateAsync(
            "k",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(new byte[] { 8 });
            },
            CancellationToken.None);

        Assert.Equal(new byte[] { 7 }, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ConcurrentMissesExecuteTheFactoryOnce()
    {
        var cache = Create();
        var calls = 0;
        var payload = new byte[] { 1 };

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () =>
                await cache.GetOrCreateAsync(
                    "k",
                    async token =>
                    {
                        Interlocked.Increment(ref calls);
                        await Task.Delay(50, token);
                        return payload;
                    },
                    CancellationToken.None)))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Equal(payload, result));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellingOneWaiterDoesNotCancelTheSharedExecution()
    {
        var cache = Create();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<byte[]> Factory(CancellationToken token)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return new byte[] { 9 };
        }

        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();

        var first = cache.GetOrCreateAsync("k", Factory, firstCts.Token).AsTask();
        await started.Task;

        var second = cache.GetOrCreateAsync("k", Factory, secondCts.Token).AsTask();

        firstCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        release.SetResult();

        Assert.Equal(new byte[] { 9 }, await second);
    }

    [Fact]
    public async Task CancellingTheLastWaiterCancelsTheSharedExecution()
    {
        var cache = Create();
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<byte[]> Factory(CancellationToken token)
        {
            started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                canceled.TrySetResult();
                throw;
            }

            return Array.Empty<byte>();
        }

        using var cts = new CancellationTokenSource();
        var call = cache.GetOrCreateAsync("k", Factory, cts.Token).AsTask();

        await started.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OversizedPayloadIsNotCached()
    {
        var cache = Create(options: Options(maxEntryBytes: 4));
        var calls = 0;
        var payload = new byte[10];

        ValueTask<byte[]> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(payload);
        }

        await cache.GetOrCreateAsync("k", Factory, CancellationToken.None);
        await cache.GetOrCreateAsync("k", Factory, CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task L2FailureFallsBackToTheFactory()
    {
        var cache = Create(new ThrowingDistributedCache());
        var calls = 0;

        var result = await cache.GetOrCreateAsync(
            "k",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(new byte[] { 3 });
            },
            CancellationToken.None);

        Assert.Equal(new byte[] { 3 }, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DisabledCacheAlwaysExecutesTheFactory()
    {
        var cache = Create(options: Options(enabled: false));
        var calls = 0;

        await cache.GetOrCreateAsync(
            "k",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(new byte[] { 1 });
            },
            CancellationToken.None);

        await cache.GetOrCreateAsync(
            "k",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(new byte[] { 1 });
            },
            CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DisabledPolicyBypassesReadAndWrite()
    {
        var l2 = new FakeDistributedCache();
        var cache = Create(l2);
        var calls = 0;
        var policy = QueryCachePolicy.Disabled;

        ValueTask<byte[]> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(new byte[] { 1 });
        }

        await cache.GetOrCreateAsync("k", Factory, policy, CancellationToken.None);
        await cache.GetOrCreateAsync("k", Factory, policy, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Null(l2.LastOptions);
    }

    [Fact]
    public async Task CustomPolicyClampsTheDistributedTtl()
    {
        var l2 = new FakeDistributedCache();
        var cache = Create(l2);
        var policy = new QueryCachePolicy(
            QueryCacheMode.Custom,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5));

        await cache.GetOrCreateAsync(
            "k",
            _ => ValueTask.FromResult(new byte[] { 1 }),
            policy,
            CancellationToken.None);

        Assert.NotNull(l2.LastOptions);
        Assert.True(
            l2.LastOptions!.AbsoluteExpirationRelativeToNow <=
            TimeSpan.FromSeconds(5));
    }

    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values =
            new(StringComparer.Ordinal);

        public DistributedCacheEntryOptions? LastOptions { get; private set; }

        public byte[]? Get(string key) =>
            _values.TryGetValue(key, out var value) ? value : null;

        public Task<byte[]?> GetAsync(
            string key,
            CancellationToken token = default) =>
            Task.FromResult(Get(key));

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            Task.CompletedTask;

        public void Remove(string key) => _values.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            LastOptions = options;
            _values[key] = value;
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("offline");

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("offline");

        public void Refresh(string key) => throw new InvalidOperationException("offline");

        public Task RefreshAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("offline");

        public void Remove(string key) => throw new InvalidOperationException("offline");

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("offline");

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("offline");

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) =>
            throw new InvalidOperationException("offline");
    }
}
