# Query Cache and Single-Flight (Option B) — Work Plan

**Status:** ✅ Implemented for Q1–Q4 and Q6. Q5 (distributed lock) remains optional and is not enabled.

**Goal:** reduce duplicate query execution with a manual, AOT-friendly cache:

* **L1:** in-process memory (`IMemoryCache`).
* **L2:** Valkey/Redis through `IDistributedCache` / `StackExchange.Redis`.
* **Single-flight:** one execution per cache key inside a process.
* **Optional:** a Valkey lock for cross-instance stampede protection.

This plan deliberately does **not** use `HybridCache`. The payload is cached as final UTF-8 JSON
(`byte[]`), so no reflection-based serializer is involved.

## Decisions

1. **Cache at the HTTP adapter layer**, not in `$let`, `PlanBuilder`, or the PostgreSQL provider.
   Caching is a host policy; the language stays deterministic.
2. **Cache bytes, not objects.** A cache entry is the final JSON response body plus a small metadata
   record. This is AOT-safe and avoids mutable object graphs.
3. **L1 first, L2 second, single-flight third.** A cache hit never reaches the query engine.
4. **Only successful results are cached.** Errors, timeouts, partial results, and authorization
   failures are never cached.
5. **Per-process single-flight first.** A distributed lock is a later, opt-in stage.
6. **Fail-open.** A missing or broken L2 must never fail a query; the request falls back to execution.
7. **Short TTL by default.** Staleness is a correctness trade-off and must be explicit.

## Non-goals

* Data-change capture or database-triggered invalidation.
* Cross-region cache replication.
* Negative caching of errors.
* Temp tables or `SELECT INTO`.
* Replacing CTEs; CTE remains the language-level materialization mechanism.
* Caching in the language core or in `PostgreSqlSource`.

## Architecture

```text
GET /query or POST /query
        │
        ▼
validate request + resolve/authorize source
        │
        ▼
build cache key
        │
        ├── L1 (IMemoryCache) hit ───────────────► return cached JSON
        │
        ├── L2 (Valkey/Redis) hit ──► fill L1 ───► return cached JSON
        │
        ▼
single-flight per key
        │
        ├── re-check L1/L2
        ├── analyze + execute query
        ├── serialize to UTF-8 JSON
        ├── write L2 with TTL
        ├── write L1 with shorter TTL
        └── return
```

Optional cross-instance stage:

```text
        ▼
try SET <key>:lock <token> NX PX <ttl>
        ├── acquired ──► re-check L2, execute, write cache, release lock
        └── not acquired ──► poll L2 with backoff
                              ├── hit ──► return
                              └── timeout ──► execute locally (fail-open)
```

## Cache key

The key must identify the **semantic request**, not the raw HTTP request.

Inputs:

* exact query text (whitespace differences create different entries; acceptable);
* source name (`null`/empty for inline-only queries);
* user variables, canonicalized by sorted key and typed value;
* tenant/user/security scope supplied by the host;
* schema/source version (deployment or explicit source version);
* cache format version;
* relevant limits if they can vary per request.

Do **not** include:

* the resolved `@source` array rows (the in-memory data would change every request);
* authorization tokens or secrets;
* the raw query text in the Redis key (hash it).

```text
fq:q:v1:<sha256-hex>
```

```csharp
var canonical = CanonicalQueryCacheKey.Create(
    query,
    sourceName,
    variables,       // excludes the server-bound @source value
    tenant,
    schemaVersion,
    limits);

var key = "fq:q:v1:" + Sha256Hex(canonical);
```

The key builder must be a pure, unit-testable function.

## Entry format

```csharp
public sealed record QueryCacheEntry(
    byte[] Payload,
    DateTimeOffset CreatedAt,
    string CacheVersion);
```

Only `Payload` is needed on the hit path. Keep the metadata minimal; it is useful for diagnostics and
future invalidation.

Limits:

* `MaxEntryBytes` (for example 256 KB) — larger results are returned but not cached;
* `MaxEntries` for L1;
* L2 TTL configured per host;
* L1 TTL must be shorter than L2 TTL.

## Single-flight

The flight table is per cache key, never a global lock.

```csharp
sealed class Flight
{
    public required Task<byte[]> Task { get; init; }
    public required CancellationTokenSource Cts { get; init; }
    public int Waiters;
}
```

Rules:

* one leader executes; followers await the same task;
* a caller cancellation removes that caller only;
* the shared task is cancelled only when the last waiter leaves and it is still running;
* the leader's failure is observed by all waiters, then the flight is removed;
* the flight is removed in `finally`, including cancellation and exceptions;
* a second cache check happens inside the leader before execution to cover a race with another
  process filling L2.

## L1 and L2 behaviour

* L1 miss → L2 read.
* L2 hit → write L1 with the shorter TTL → return.
* L2 read failure → treat as miss; log at debug/warning level.
* L2 write failure → return the result; never fail the query.
* L1 write failure → return the result.
* L1 entries carry a size budget; eviction follows `IMemoryCache` limits.
* Add small TTL jitter to avoid synchronized expiry.

Suggested starting values:

| Setting | Default |
| ------- | ------- |
| L1 TTL | 1–2 seconds |
| L2 TTL | 15–60 seconds |
| Entry size limit | 256 KB |
| Single-flight | enabled |
| Distributed lock | disabled |

## Cache invalidation

1. **TTL is the primary invalidation.** Document the maximum acceptable staleness.
2. **Version prefix** for deployments or schema changes: `fq:q:v2:...`.
3. **Per-source version** when the host can detect a change; include it in the key.
4. **No `KEYS`/`SCAN` invalidation** in production.
5. If a host needs strict consistency, it must disable the cache for that source.

## Adapter integration

The HTTP endpoint currently builds an `IResult` after execution. Cache integration should refactor the
execution path into a result payload:

```csharp
public interface IQueryResultCache
{
    ValueTask<byte[]?> TryGetAsync(string key, CancellationToken ct);

    ValueTask SetAsync(string key, byte[] payload, CancellationToken ct);

    ValueTask<byte[]> GetOrCreateAsync(
        string key,
        Func<CancellationToken, ValueTask<byte[]>> factory,
        CancellationToken ct);
}
```

Endpoint flow:

1. Validate request size and parameters.
2. Resolve and authorize the source.
3. Build the key (source name and user variables only).
4. If caching is enabled, call `GetOrCreateAsync`.
5. The factory performs the existing analysis/execution and returns UTF-8 JSON bytes.
6. Return `Results.Bytes(payload, "application/json")`.

When caching is disabled, keep the existing path unchanged.

## Valkey/Redis

* Use `IDistributedCache` for simple GET/SET, or `StackExchange.Redis` directly for more control.
* `AbortOnConnectFail = false`; configure reconnect and command timeouts.
* Store `byte[]` directly; do not use `IDistributedCache` with a custom object serializer.
* Avoid reflection-based `ScriptEvaluate` overloads with anonymous objects.
* For the optional lock, use explicit `RedisKey[]`/`RedisValue[]` parameters.
* Use a dedicated key prefix, for example `funquery:q:v1:`.
* Never use `FLUSHDB` or `KEYS` in application code.

## AOT

The cache must be AOT-safe:

* payload is `byte[]`; no `System.Text.Json` reflection;
* `IMemoryCache` has no reflection-based serialization;
* `IDistributedCache` stores bytes;
* `StackExchange.Redis` basic commands are used; Lua parameters are explicit arrays;
* publish both hosts with Native AOT and check `IL2026`, `IL3050`, `IL2104`, `IL3053`;
* if the Redis client emits warnings, either pin a compatible version or keep the cache out of the AOT
  host until the dependency is clean.

## Stages

| Stage | Name | Depends on | Size | Status |
| ----- | ---- | ---------- | ---- | ------ |
| Q1 | Options, key builder, entry and interfaces | — | S | ✅ |
| Q2 | L1 memory cache and per-process single-flight | Q1 | M | ✅ |
| Q3 | L2 Valkey/Redis, fail-open, metrics | Q2 | M | ✅ |
| Q4 | HTTP adapter integration and source/tenant scoping | Q3 | M | ✅ |
| Q5 | Optional distributed lock for multi-instance deployments | Q3 | S | ⏳ |
| Q6 | Tests, docs, AOT publish and smoke test | Q1–Q4 | S | ✅ |

### Q1 — Options, key builder, entry and interfaces

* Add `QueryCacheOptions`: enabled, TTLs, entry size, entry count, prefix, version, cache scope hook.
* Add `IQueryCacheKeyBuilder` and a pure canonical-key implementation.
* Add `QueryCacheEntry` and `IQueryResultCache`.
* Unit-test key stability, variable ordering, source exclusion, tenant isolation, and version changes.

### Q2 — L1 and single-flight

* Implement `IMemoryCache` L1 with `SizeLimit` and entry size.
* Implement the flight table with ref-counted cancellation.
* Test: 100 concurrent identical requests execute once; different keys run independently.
* Test: caller cancellation does not cancel other waiters; last waiter does.
* Test: exceptions are shared and the flight is removed.

### Q3 — L2 and resilience

* Register `IDistributedCache` with Valkey/Redis in the sample hosts (opt-in).
* Implement L2 read/write with fail-open behavior.
* Add TTL and small jitter.
* Add metrics: `l1_hit`, `l2_hit`, `miss`, `coalesced`, `cache_error`.
* Test: L2 hit fills L1; L2 outage falls back to execution; write failure does not fail the request.

### Q4 — Adapter integration

* Refactor the endpoint execution path to return `byte[]`.
* Resolve/authorize the source before the cache lookup.
* Build the key from user variables only; excluding the bound `@source` value.
* Add a `CacheScope` hook for tenant/user identity.
* Test GET and POST with the same query/source/variables share a key.
* Test that errors and oversized results are not cached.

### Q5 — Optional distributed lock

* Add `IDistributedLock` backed by Valkey `SET NX PX` and a compare-and-delete Lua script.
* Waiters poll L2 with backoff, then fail open.
* Lock TTL must exceed `MaxExecutionTime` plus a buffer.
* Test concurrency across two simulated cache instances.

### Q6 — Tests, docs, AOT

* Add an end-to-end test through the sample host.
* Document options, expected staleness, metrics, and failure behavior.
* Publish both hosts with Native AOT and run cache miss/hit smoke tests.
* Record the final test count.

## Tests

| Case | Expected |
| ---- | -------- |
| L1 hit | no L2 read, no execution |
| L2 hit | L1 filled, no execution |
| Miss | one execution per key |
| 100 concurrent identical requests | factory called once |
| Cancellation of one waiter | other waiters continue |
| Last waiter cancels | shared execution cancelled |
| Leader throws | all waiters observe the error; flight removed |
| L2 read fails | query executes |
| L2 write fails | query succeeds |
| Entry too large | not cached |
| Different variables | different keys |
| Different tenant | different keys |
| GET vs POST same request | same key |
| Error response | not cached |
| TTL expiry | next request executes again |
| Multiple instances + lock | only one executes; others reuse L2 |

## Definition of Done

* Cache is opt-in and disabled by default.
* L1, L2, and single-flight work as described.
* Cache failures never fail a query.
* Only successful results are cached.
* Tenant/source/version isolation is tested.
* No reflection-based serialization; AOT publish and smoke test pass.
* Documentation states the staleness and invalidation trade-offs.
* Full test suite passes.

## Risks and open decisions

1. **Staleness.** TTL caching returns data that may be old. Hosts must choose the TTL per source.
2. **Invisible data changes.** FunQuery sources are arbitrary; a generic cache cannot know when data
   changes. Start with TTL only; add explicit source versions later.
3. **Tenant leakage.** A missing tenant/auth component in the key is a security bug. Make the scope
   hook mandatory when caching is enabled in a multi-tenant host.
4. **Cancellation races.** The flight table must be ref-counted and cleaned up carefully.
5. **L2 availability.** Cache must fail open; a cache outage cannot become an API outage.
6. **Memory pressure.** L1 needs a size limit; entries must be bounded.
7. **AOT dependencies.** Verify `Microsoft.Extensions.Caching.StackExchangeRedis`/`StackExchange.Redis`
   warnings before shipping the cache in the AOT host.
8. **Distributed lock trade-off.** The lock reduces duplicate work but adds latency and failure modes.
   It should remain opt-in.

## Result

Implemented for Q1–Q4 and Q6:

* New `src/FunQuery.Caching` project with `QueryCacheOptions`, `QueryCacheKeyBuilder`,
  `QueryResultCache`, `QueryCacheMetrics`, and `AddFunQueryQueryCache`.
* L1 uses `IMemoryCache` with a size limit; L2 is optional through `IDistributedCache`.
* Payloads are cached as UTF-8 `byte[]`; no reflection-based serializer is used.
* Single-flight is per cache key with ref-counted cancellation. A caller cancellation does not cancel
  other waiters; the shared execution is cancelled only when the last waiter leaves.
* L2 read/write failures are fail-open: the query still executes and returns its result.
* The HTTP endpoint builds the key from the query, source name, user variables, and an optional
  `IQueryCacheScopeProvider` scope. The bound `@source` rows are not hashed.
* Errors, oversized results, and disabled cache paths are never cached.
* Sample hosts register the cache through configuration:
  `Cache:Enabled`, `Cache:Valkey`, `Cache:LocalTtlSeconds`, `Cache:DistributedTtlSeconds`,
  `Cache:MaxEntryBytes`, `Cache:MaxEntries`, `Cache:KeyPrefix`, and `Cache:Version`.
* The sample hosts use `AddStackExchangeRedisCache` when `Cache:Valkey` or
  `ConnectionStrings:Valkey` is configured. Without L2 the cache still works as L1-only.
* Tests: key stability/isolation, L1/L2 behavior, concurrency, cancellation, oversize entries,
  L2 failure, disabled cache, and HTTP-level cache hits. The full suite is **1186 tests**.
* Native AOT publish of both hosts succeeds with the Redis package. The published PostgreSQL host
  served `/playground/` and cached a query result in Valkey (key prefix `funquery:fq:q:v1:`).
* Q5 remains pending: the distributed lock is not implemented, so multi-instance duplicate execution
  can still occur. This is deliberate; the plan marks it optional.
