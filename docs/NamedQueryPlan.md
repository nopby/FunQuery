# Named Queries (`$named`) — Work Plan

**Status:** 🚧 N1–N6 implemented; N7–N8 pending.

**Goal:** let a client invoke a pre-approved query stored in `funquery.named_queries` by key, without
sending query text and without exposing the catalog as a query source.

```text
$named('orders-over-100')
```

is equivalent to:

```http
GET /query?key=orders-over-100&v.min=100
```

## Decisions

1. **Table:** `funquery.named_queries`.
2. **Function:** `$named('key')`, top-level only, string literal only, no chaining in v1.
3. **Canonical HTTP form:** `?key=<key>`. `$named('key')` is sugar over the same resolver.
4. **Parameters:** `v.<name>` on GET, `variables` on POST. No auto-forwarding of arbitrary query
   parameters.
5. **Source:** taken from the catalog row. The client cannot supply or override `source`.
6. **Cache:** policy comes from the catalog row; no `$cache`/`$nocache` in the language.
7. **Resolution point:** host/adapter, before the provider plan. Providers never see `$named`.
8. **Catalog is private:** it is never registered as an `IQuerySource`.
9. **Ownership scope:** every catalog row has `app_id` and `tenant_id`. `tenant_id` isolates tenants;
   `app_id` reserves the application dimension for future multi-app deployments. Global/unspecified
   values use the reserved literal `global`.
10. **Admin API:** a host-mounted CRUD API is in scope, protected by an admin authorization hook.
11. **Invalidation:** PostgreSQL `LISTEN/NOTIFY` is required for catalog changes, with a short TTL as a
    fallback.

## Non-goals

* `$exec` or arbitrary query-text execution.
* Nested `$named` calls or `$source($named(...))`.
* Chaining after `$named` in v1.
* Direct `&min=100` parameters in v1 (the `parameters` column reserves the shape for later).
* Cross-tenant or cross-app key sharing.
* Recursive named queries.
* Replacing the existing ad-hoc `q` endpoint.
* A database-agnostic admin UI; the CRUD API is the supported admin surface in v1.

## Architecture

```text
GET /query?key=...
GET /query?q=$named('key')
        │
        ▼
parse/analyze the invocation
        │
        ▼
INamedQueryStore
  app + tenant + key → NamedQueryEntry
        │
        ├─ disabled / missing / forbidden → structured error
        │
        ▼
entry: query_text + source_name + cache policy + version
        │
        ▼
QueryEngine.Prepare(query_text, variables)
        │
        ▼
cache layer (L1/L2/single-flight)
        │
        ▼
provider executor
        │
        ▼
JSON response
```

The catalog lookup and `$named` resolution happen before provider execution. The provider receives an
ordinary analyzed query.

## Data model

```sql
CREATE SCHEMA IF NOT EXISTS funquery;

CREATE TABLE funquery.named_queries (
    app_id          text        NOT NULL DEFAULT 'global',
    tenant_id       text        NOT NULL DEFAULT 'global',
    query_key       text        NOT NULL,
    query_text      text        NOT NULL,
    source_name     text        NULL,
    enabled         boolean     NOT NULL DEFAULT true,
    cache_mode      text        NOT NULL DEFAULT 'default'
                    CHECK (cache_mode IN ('none', 'default', 'custom')),
    l1_ttl_seconds  integer     NULL CHECK (l1_ttl_seconds IS NULL OR l1_ttl_seconds > 0),
    l2_ttl_seconds  integer     NULL CHECK (l2_ttl_seconds IS NULL OR l2_ttl_seconds > 0),
    version         integer     NOT NULL DEFAULT 1,
    parameters      jsonb       NULL,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      text        NULL,

    PRIMARY KEY (app_id, tenant_id, query_key)
);

CREATE INDEX ix_named_queries_enabled
    ON funquery.named_queries (app_id, tenant_id, enabled);
```

Notes:

* `app_id` is the application/namespace dimension; use `global` when the host has only one app.
* `tenant_id` is the tenant isolation dimension; use `global` for app-wide entries.
* The composite key allows the same `query_key` in different apps and tenants.
* `query_text` is a FunQuery query, not raw SQL.
* `source_name` must refer to a registered host source. It is validated when the entry is loaded.
* `parameters` is reserved for declared parameter metadata; v1 still uses `v.<name>`.
* The table must not be exposed as an `IQuerySource`.

## `$named` semantics

```text
$named('orders-over-100')
```

Rules:

* exactly one argument;
* the argument must be a non-empty string literal;
* the key is case-sensitive;
* only allowed as the complete top-level expression;
* no chaining, no nesting, no variables as key;
* the stored query must not itself contain `$named` in v1;
* if the host has no named-query store, return `NAMED_QUERIES_UNAVAILABLE`.

Invalid:

```text
$named(@key)
$named('')
$named('a', 'b')
$named('a').$filter(id gt 1)
$source($named('a'))
```

## HTTP contract

### GET by key

```http
GET /query?key=orders-over-100&v.min=100
```

### GET through the function

```http
GET /query?q=$named('orders-over-100')&v.min=100
```

Both use the same resolver and cache key. Exactly one of `key` or `q` is allowed.

### POST

```json
{
  "key": "orders-over-100",
  "variables": {
    "min": 100
  }
}
```

or:

```json
{
  "query": "$named('orders-over-100')",
  "variables": {
    "min": 100
  }
}
```

Rules:

* `key` and `q`/`query` cannot be supplied together;
* `source` must not be supplied with a named query; it comes from the catalog;
* unknown top-level fields remain rejected as today.

## Parameters

v1 uses the existing variable conventions:

```text
GET  ?key=orders-over-100&v.min=100&v.status=open
POST { "variables": { "min": 100, "status": "open" } }
```

The stored query uses `@min` and `@status`. Missing variables fail during analysis with
`UNDEFINED_VARIABLE`. Type inference follows `QueryVariableValues`.

The optional `parameters` column can later declare:

```json
[
  { "name": "min", "type": "int", "required": true },
  { "name": "status", "type": "string", "required": false, "default": "open" }
]
```

That would allow direct `&min=100` and better validation, but it is not part of v1.

## Cache integration

The existing L1/L2/single-flight cache is reused.

Per-entry mapping:

| `cache_mode` | Behaviour |
| ------------ | --------- |
| `none` | bypass read and write |
| `default` | host `LocalTtl` / `DistributedTtl` |
| `custom` | clamp `l1_ttl_seconds` / `l2_ttl_seconds` to host maxima |

Cache key:

```text
fq:named:v1:<sha256(
    app_id
    + tenant_id
    + query_key
    + version
    + query_text_hash
    + canonical user variables
)>
```

* `query_text_hash` ensures an edited query cannot reuse its old entry even if `version` was not bumped.
* TTL is not part of the key.
* `source_name` is not repeated because it is part of the catalog entry identity; include it in the
  hash if a key can change source without changing `query_text`.
* The cache key does not contain `$named` or the raw query text.

`cache_mode=none` skips the cache entirely. There is no `$nocache` function.

## Permissions

Host-provided access check:

```csharp
public interface INamedQueryAccessProvider
{
    bool CanInvoke(string appId, string tenantId, string queryKey);
}
```

Checks, in order:

1. user is authenticated (if the host requires it);
2. catalog entry exists;
3. entry is enabled;
4. `app_id` and `tenant_id` match the caller context;
5. `source_name` is registered and allowed for the caller;
6. caller may invoke the key.

The catalog table is never exposed as a query source. Management uses the opt-in admin API below.

## Admin CRUD API

The catalog is managed through an opt-in host API, not through the query language or through a public
`IQuerySource`.

The adapter exposes two integration options:

1. **Service only:** the host injects `INamedQueryAdminService` into its existing admin
   controllers/endpoints. This avoids any route conflict.
2. **Mapped endpoints:** the host explicitly calls `MapFunQueryNamedQueryAdmin(...)` with its own route
   prefix. The adapter never maps these endpoints automatically.

Recommended default route:

```text
/funquery/admin/named-queries
```

Do **not** default to a generic `/admin/named-queries`; many Web API projects already own `/admin`.
The route is configurable through `NamedQueryAdminOptions.Route`.

```csharp
builder.Services.AddFunQueryNamedQueryAdmin(options =>
{
    options.Route = "/api/funquery/admin/named-queries";
    options.RequireAuthorization = true; // host policy
});

app.MapFunQueryNamedQueryAdmin();
```

The host supplies an `INamedQueryAdminAuthorizer` (or equivalent policy) that checks the caller for
each operation. The API must be unreachable without that check.

If `MapFunQueryNamedQueryAdmin` is called twice, or the selected route conflicts with an existing
endpoint, fail fast at startup with a clear message rather than serving an ambiguous route.

### Endpoints

| Method | Path | Purpose |
| ------ | ---- | ------- |
| `GET` | `/admin/named-queries?app=&tenant=` | list entries, paged |
| `GET` | `/admin/named-queries/{key}?app=&tenant=` | read one entry |
| `POST` | `/admin/named-queries` | create an entry |
| `PUT` | `/admin/named-queries/{key}?app=&tenant=` | replace an entry |
| `PATCH` | `/admin/named-queries/{key}?app=&tenant=` | update selected fields |
| `DELETE` | `/admin/named-queries/{key}?app=&tenant=` | disable or delete |

The table above uses the default relative paths; the actual paths are prefixed by the configured
`NamedQueryAdminOptions.Route`.

`app` and `tenant` are admin-supplied identity dimensions, not user query parameters. Use `global`
when the host has only one app/tenant.

### Write rules

* Validate the key format (for example `[A-Za-z0-9][A-Za-z0-9._-]{0,127}`).
* Parse and analyze `query_text` against the registered source and schema.
* Reject `$named` inside `query_text` in v1.
* Reject `source_name` values that are not registered.
* Validate `cache_mode` and TTLs; `custom` requires TTLs and they are clamped at runtime.
* On update: increment `version`, set `updated_at`/`updated_by`, and emit a `NOTIFY`.
* On delete: emit a `NOTIFY` so catalog caches are invalidated.
* Use `If-Match`/version or an equivalent optimistic-concurrency check to avoid lost updates.
* Never return connection strings, secrets, or raw SQL.
* Return the `query_text` only to authorized admins.

### Response shape

```json
{
  "appId": "global",
  "tenantId": "global",
  "key": "orders-over-100",
  "query": "$source(@source).$filter(total gt @min).$select(id, customer, total)",
  "source": "orders",
  "enabled": true,
  "cacheMode": "custom",
  "l1TtlSeconds": 2,
  "l2TtlSeconds": 30,
  "version": 3,
  "updatedAt": "2026-11-08T10:00:00Z",
  "updatedBy": "admin@example.com"
}
```

### Admin errors

| Situation | Code | HTTP |
| --------- | ---- | ---- |
| No admin policy / unauthorized | `NAMED_QUERY_ADMIN_FORBIDDEN` | 403 |
| Unknown key | `UNKNOWN_NAMED_QUERY` | 404 |
| Duplicate key | `NAMED_QUERY_EXISTS` | 409 |
| Invalid query text | `INVALID_NAMED_QUERY` | 422 |
| Invalid key/cache mode/TTL | `INVALID_REQUEST` | 400 |
| Version conflict | `NAMED_QUERY_CONFLICT` | 409 |

## Authorization and SSO

FunQuery does not implement login or token validation. The host owns authentication; FunQuery only
exposes authorization seams.

| Seam | Question it answers |
| ---- | ------------------- |
| `INamedQueryAdminAuthorizer` | May this caller create/update/delete catalog entries? |
| `INamedQueryAccessProvider` | May this caller invoke this app/tenant/key? |
| `IQueryCacheAccessProvider` | May this caller read/write/configure cache? |
| `IQueryCacheScopeProvider` | What app/tenant scope belongs to this request? |

### Keycloak / OIDC / SSO

Keycloak, Azure AD, Auth0, Okta, and other OIDC providers plug in through standard ASP.NET Core
authentication:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://keycloak.example.com/realms/funquery";
        options.Audience = "funquery-api";
        options.RequireHttpsMetadata = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("FunQueryAdmin", policy =>
        policy.RequireRole("funquery-admin"));

    options.AddPolicy("FunQueryQuery", policy =>
        policy.RequireAuthenticatedUser());
});
```

Endpoint-level policy for the mapped admin API:

```csharp
app.MapFunQueryNamedQueryAdmin()
    .RequireAuthorization("FunQueryAdmin");
```

Service-level hook for an existing admin controller:

```csharp
public sealed class KeycloakNamedQueryAdminAuthorizer(
    IAuthorizationService authorization) : INamedQueryAdminAuthorizer
{
    public async ValueTask<bool> CanManageAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var result = await authorization.AuthorizeAsync(
            context.User,
            "FunQueryAdmin");

        return result.Succeeded;
    }
}
```

Keycloak roles/scopes are mapped to claims by the JWT handler; the hook only reads the resulting
`ClaimsPrincipal`. SAML or another SSO mechanism works the same way: the host authentication middleware
populates the principal before FunQuery sees the request.

### App and tenant identity

* `app_id` and `tenant_id` must come from the validated principal, a server-side mapping, or a trusted
  admin context; never from an arbitrary client header or query parameter.
* A tenant claim can be checked by `INamedQueryAccessProvider` before the key is loaded.
* For admins managing multiple tenants, require an explicit server-side authorization decision for the
  requested `(app_id, tenant_id)` pair.

### Rules

* Validate issuer, audience, signature, and expiry using the host's authentication handler.
* Do not put raw tokens in cache keys or logs.
* Keep admin and query audiences/policies separate.
* Use least privilege: read-only consumers should not receive admin or cache-configure permissions.
* If the host has no authentication, the admin API must remain unmapped; do not fall back to anonymous
  admin access.

## Catalog lookup caching

The catalog read must not hit PostgreSQL on every request.

* L1 memory cache: `(app_id, tenant_id, key) → entry`, short TTL (for example 30 seconds) as a safety
  net.
* Optional L2 cache if the host wants cross-instance sharing.
* **Required invalidation:** PostgreSQL `LISTEN/NOTIFY`. A trigger on `funquery.named_queries` publishes
  `app_id`, `tenant_id`, `query_key`, `version`, and the operation. Each host instance listens and
  invalidates the matching L1/L2 lookup entry.
* TTL remains a fallback for missed notifications or listener downtime.
* If the catalog database is unavailable and the entry is not cached, return `503`; never fall back to
  an ad-hoc query.

### PostgreSQL trigger and listener

```sql
CREATE OR REPLACE FUNCTION funquery.notify_named_query_change()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM pg_notify(
        'funquery_named_queries',
        json_build_object(
            'operation', TG_OP,
            'app_id', COALESCE(NEW.app_id, OLD.app_id),
            'tenant_id', COALESCE(NEW.tenant_id, OLD.tenant_id),
            'query_key', COALESCE(NEW.query_key, OLD.query_key),
            'version', COALESCE(NEW.version, OLD.version)
        )::text
    );
    RETURN COALESCE(NEW, OLD);
END;
$$;

CREATE TRIGGER named_queries_notify
AFTER INSERT OR UPDATE OR DELETE ON funquery.named_queries
FOR EACH ROW EXECUTE FUNCTION funquery.notify_named_query_change();
```

Listener requirements:

* one dedicated PostgreSQL connection per host instance;
* reconnect with backoff;
* re-subscribe after reconnect;
* invalidate the L1 catalog lookup and, if used, the L2 catalog key;
* payload excludes `query_text`; do not publish sensitive query bodies through `NOTIFY`;
* admin writes from the CRUD API use the same trigger, so all instances converge.

## Errors

| Situation | Code | HTTP |
| --------- | ---- | ---- |
| `$named` is not top-level | `INVALID_NAMED_QUERY_POSITION` | 400 |
| `$named` argument is not a string literal | `TYPE_MISMATCH` | 400 |
| `$named` argument is empty | `INVALID_NAMED_QUERY_KEY` | 400 |
| `key` and `q`/`query` both supplied | `INVALID_REQUEST` | 400 |
| `source` supplied with a named query | `INVALID_REQUEST` | 400 |
| Key not found | `UNKNOWN_NAMED_QUERY` | 404 |
| Key disabled | `NAMED_QUERY_DISABLED` | 404 |
| App/tenant/access denied | `NAMED_QUERY_FORBIDDEN` | 403 |
| Catalog query invalid | `INVALID_NAMED_QUERY` | 500 |
| Catalog database unavailable | `NAMED_QUERY_UNAVAILABLE` | 503 |
| Nested/recursive named query | `NAMED_QUERY_CYCLE` | 400 |

Error messages must not list available keys.

## Implementation stages

| Stage | Name | Depends on | Size | Status |
| ----- | ---- | ---------- | ---- | ------ |
| N1 | Catalog schema, entry model, store interface | — | S | ⏳ |
| N2 | PostgreSQL store, lookup cache, and `LISTEN/NOTIFY` | N1 | M | ⏳ |
| N3 | Admin CRUD API and validation | N1, N2 | M | ⏳ |
| N4 | `$named` recognition, validation, and top-level rule | N1 | M | ✅ |
| N5 | HTTP `?key=` and `$named` resolution | N2, N4 | M | ✅ |
| N6 | Permission, app/tenant, and source checks | N5 | S | ✅ |
| N7 | Cache policy integration and invalidation | N5 | M | ⏳ |
| N8 | Tests, docs, AOT publish and smoke test | N1–N7 | S | ⏳ |

### N1 — Catalog schema and model

* Add the SQL migration/seed for `funquery.named_queries` plus the `LISTEN/NOTIFY` trigger.
* Add `NamedQueryEntry` and `INamedQueryStore`.
* Add `NamedQueryEntry` validation: key, query text, source, cache mode, TTLs, app/tenant scope.
* Do not register the table as a source.

### N2 — Store, lookup cache, and invalidation

* Implement `PostgreSqlNamedQueryStore` with parameterized SQL.
* Add a short-lived in-memory lookup cache keyed by `(app_id, tenant_id, key)`.
* Add the PostgreSQL listener service, reconnect/backoff, and invalidation on
  `funquery_named_queries`.
* Keep TTL as a fallback for missed notifications.
* Handle catalog errors without leaking SQL or query text.
* Test lookup, disabled entries, app/tenant isolation, invalidation by version, and listener reconnect.

### N3 — Admin CRUD API

* Add `INamedQueryAdminService` so a host can call CRUD from its own admin controllers.
* Add the opt-in `MapFunQueryNamedQueryAdmin` mapping with a configurable route prefix.
* Add the admin authorization hook.
* Implement list/read/create/replace/patch/delete.
* Validate key format, query text, source, cache mode, TTLs, and app/tenant scope.
* Reject `$named` inside stored queries in v1.
* Increment `version` on update and emit the notification through the trigger.
* Add optimistic concurrency (`If-Match`/version conflict).
* Fail fast on duplicate admin route mappings or conflicts with existing endpoints.
* Never expose the catalog through the normal query endpoints.
* Test authorization, duplicate keys, validation, conflicts, update visibility, and route conflict.

### N4 — `$named` language support

* Register/special-case `$named` in the core analyzer.
* Validate: top-level only, one string literal, non-empty key.
* Add `QueryPlan.NamedQueryKey` or equivalent metadata.
* Ensure the provider plan never receives `$named`.
* Test invalid positions and arguments.

### N5 — HTTP resolution

* Add `?key=` and `$named` handling in the adapter before `QueryEngine.Prepare`.
* Reject `key` + `q`/`query` together and `source` with named queries.
* Resolve the entry, then analyze `query_text` with the entry's source and request variables.
* Keep the existing ad-hoc path unchanged.
* Test GET/POST parity and `$named`/`?key=` equivalence.

### N6 — Permissions and source checks

* Add `INamedQueryAccessProvider`.
* Validate app, tenant, key, enabled flag, source registration, and caller access.
* Never expose the catalog list in errors.
* Test cross-app/tenant denial and disabled keys.

### N7 — Cache integration

* Build the named-query cache key from app, tenant, key, version, query hash, and variables.
* Apply `cache_mode`, TTL clamping, `none` bypass, and L1/L2/single-flight.
* Ensure editing `query_text` changes the key even without a version bump.
* Test cache hits, TTL clamping, bypass, and invalidation.

### N8 — Docs, AOT, close-out

* Document the table, admin workflow, `$named`, `?key=`, variables, cache modes, and errors.
* Add a sample catalog row and curl examples.
* Publish AOT with the new store/listener; smoke-test missing, hit, cached, and admin-created queries.
* Update this plan with a Result section.

## Tests

| Case | Expected |
| ---- | -------- |
| `$named('key')` | resolves and executes stored query |
| `?key=key` | same result as `$named` |
| `$named(@key)` | `TYPE_MISMATCH` |
| `$named('key').$filter(...)` | `INVALID_NAMED_QUERY_POSITION` |
| `$named('missing')` | `UNKNOWN_NAMED_QUERY` |
| disabled key | `NAMED_QUERY_DISABLED` |
| wrong app/tenant | `NAMED_QUERY_FORBIDDEN` |
| source not registered | `INVALID_NAMED_QUERY` |
| `key` + `q` together | `INVALID_REQUEST` |
| `source` with named query | `INVALID_REQUEST` |
| variables `v.min` | typed and passed to stored query |
| missing variable | `UNDEFINED_VARIABLE` |
| cache `none` | no read/write |
| cache `custom` | TTL clamped to host |
| query text edited | cache key changes |
| channel A cache hit | no DB execution |
| `$named` in stored query | `NAMED_QUERY_CYCLE` |
| catalog table queried as source | not registered / impossible |
| admin CRUD create | entry appears for `?key=` |
| admin CRUD update | version bumped, notification emitted |
| admin CRUD unauthorized | `NAMED_QUERY_ADMIN_FORBIDDEN` |
| duplicate key | `NAMED_QUERY_EXISTS` |
| version conflict | `NAMED_QUERY_CONFLICT` |
| host-owned admin controller | uses `INamedQueryAdminService`, no route conflict |
| duplicate admin route | startup failure with a clear message |
| `LISTEN/NOTIFY` update | every instance invalidates its lookup cache |
| listener reconnect | re-subscribes and keeps serving |
| AOT publish | no warnings from new store/listener/admin API |

## Definition of Done

* `$named('key')` and `?key=key` work against a catalog entry.
* The client cannot supply `source` or arbitrary query text.
* Parameters use `v.<name>` / `variables`.
* Cache policy comes from the catalog, with host clamping.
* Permissions and app/tenant isolation are enforced before execution.
* The CRUD API is available only behind the admin authorization hook.
* Catalog changes invalidate every instance through `LISTEN/NOTIFY`, with TTL fallback.
* The catalog table is never exposed as a query source.
* Errors use stable codes and never list keys.
* Full test suite, PostgreSQL integration, and Native AOT smoke test pass.
* Documentation and sample catalog row are included.

## Progress

N1–N3 are implemented:

* N1: `db/named_queries.sql` with schema, table, index, trigger; `NamedQueryEntry`,
  `NamedQueryStoreOptions`, and `INamedQueryStore`.
* N2: `PostgreSqlNamedQueryStore`, `NamedQueryLookupCache`, `NamedQueryChangeListener`, and live
  PostgreSQL tests for CRUD/versioning and `LISTEN/NOTIFY` invalidation.
* N3: `NamedQueryAdminService`, `INamedQueryAdminAuthorizer`, configurable
  `MapFunQueryNamedQueryAdmin`, route-conflict detection, host-owned authorization hook, and endpoint
  integration tests.
* N4: core `$named('key')` recognition with top-level-only, string-literal, non-empty-key validation;
  `QueryPlan.NamedQueryKey` metadata and syntax tests.
* N5: `?key=` and `$named('key')` resolution in the HTTP adapter, catalog source binding, GET/POST
  parity, key/source/query conflict validation, and a PostgreSQL host smoke test.
* N6: `INamedQueryAccessProvider`, default allow-all hook, app/tenant scope plumbing, 403 mapping,
  catalog source-registration checks, and scope/permission tests.
* Full suite: **1229 tests**. The catalog schema has been applied to the local development database.
* N7–N8 (cache integration, docs/AOT close-out) remain pending.

## Risks and open decisions

1. **Catalog availability.** A catalog outage can block named queries. Cache entries aggressively and
   return `503` instead of falling back to arbitrary queries.
2. **Key enumeration.** Keys are not secrets; authorization must be enforced per key.
3. **Variable cardinality.** `v.*` can create many cache entries for one key. Add per-key limits or
   disable caching for high-cardinality variables.
4. **Query text edits.** Use `query_text_hash` in the cache key; do not rely only on `version`.
5. **Nested named queries.** Deferred; recursion/cycles need a depth limit and a resolver stack.
6. **Admin API security.** The CRUD route is opt-in and must be unreachable without an admin
   authorization hook. Admin identity dimensions (`app_id`, `tenant_id`) must not be inferred from
   untrusted client input.
7. **Listener reliability.** `LISTEN/NOTIFY` is best-effort; keep the short TTL fallback and
   reconnect/re-subscribe logic.
8. **Parameter schema.** The `parameters` column is reserved; direct `&min=` is a follow-up.
