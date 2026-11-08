# Named Queries

Named queries let a client run a pre-approved FunQuery stored in the database by key. The client sends a
key, not query text. The catalog also owns the source binding and cache policy.

**Status:** implemented for PostgreSQL. See [`NamedQueryPlan.md`](NamedQueryPlan.md) for the staged plan
and implementation history.

## Why use them

* Clients cannot send arbitrary query text.
* Queries, source bindings, and cache policy are admin-managed.
* Cache keys are stable across clients.
* A query can be changed without redeploying the host.
* Errors and access can be audited per key.

The catalog table is **not** registered as an `IQuerySource` and must never be exposed through
`$source`.

## Schema

Apply:

```bash
psql "$FUNQUERY_POSTGRES" -f db/named_queries.sql
```

The script creates:

```sql
CREATE SCHEMA IF NOT EXISTS funquery;

CREATE TABLE funquery.named_queries (
    app_id          text        NOT NULL DEFAULT 'global',
    tenant_id       text        NOT NULL DEFAULT 'global',
    query_key       text        NOT NULL,
    query_text      text        NOT NULL,
    source_name     text        NULL,
    enabled         boolean     NOT NULL DEFAULT true,
    cache_mode      text        NOT NULL DEFAULT 'default',
    l1_ttl_seconds  integer     NULL,
    l2_ttl_seconds  integer     NULL,
    version         integer     NOT NULL DEFAULT 1,
    parameters      jsonb       NULL,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    updated_by      text        NULL,
    PRIMARY KEY (app_id, tenant_id, query_key)
);
```

It also creates the `funquery_named_queries` `LISTEN/NOTIFY` trigger used to invalidate host lookup
caches.

### Sample row

```sql
INSERT INTO funquery.named_queries
    (app_id, tenant_id, query_key, query_text, source_name,
     cache_mode, l1_ttl_seconds, l2_ttl_seconds)
VALUES
    ('global', 'global', 'orders-over-100',
     '$source(@source).$filter(total gt @min).$select(id, customer, total)',
     'orders', 'custom', 2, 30);
```

`source_name` must refer to a source registered by the host. The stored query is FunQuery, never raw
SQL.

## HTTP usage

### By key

```http
GET /query?key=orders-over-100&v.min=100
```

### By function

```http
GET /query?q=$named('orders-over-100')&v.min=100
```

Both forms use the same resolver and the same result-cache entry.

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

* `key` and `q`/`query` cannot be sent together.
* `source` cannot be sent with a named query; it comes from the catalog.
* `$named` must be the complete top-level query.
* Parameters use `v.<name>` (GET) or `variables` (POST).
* The key is case-sensitive.

## `$named` rules

```text
$named('orders-over-100')
```

* exactly one argument;
* non-empty string literal;
* top-level only;
* no chaining;
* no nested `$named`;
* resolved by the host before the provider.

Invalid:

```text
$named(@key)
$named('')
$named('a', 'b')
$named('a').$filter(id gt 1)
$source($named('a'))
```

## Cache policy

`cache_mode` controls the existing L1/L2/single-flight cache:

| Mode | Behaviour |
| ---- | --------- |
| `none` | bypass read and write |
| `default` | host `LocalTtl` / `DistributedTtl` |
| `custom` | catalog `l1_ttl_seconds` / `l2_ttl_seconds`, clamped to host maxima |

Clamping is `min(catalog TTL, host TTL)`; a named query can shorten but never extend the host limit.

Result-cache key:

```text
fq:q:v1:<sha256(
    app_id + tenant_id + query_key + version +
    query_text + source_name + canonical variables
)>
```

Changing `query_text` or `version` changes the key, so old entries are not reused. Catalog lookup
caches are invalidated by the PostgreSQL trigger.

## App and tenant scope

`app_id` and `tenant_id` default to `global`. The host resolves them through
`INamedQueryScopeProvider`; the default is `global/global`.

For multi-tenant hosts, derive them from validated claims, not from an arbitrary client header or query
parameter.

## Access control

### Invocation

```csharp
public interface INamedQueryAccessProvider
{
    ValueTask<bool> CanInvokeAsync(
        HttpContext context,
        NamedQueryScope scope,
        string key,
        CancellationToken cancellationToken);
}
```

The default `AllowAllNamedQueryAccessProvider` is for development/single-tenant hosts. Replace it with
a Keycloak/OIDC policy check in production.

A denied invocation returns:

```json
{ "code": "NAMED_QUERY_FORBIDDEN", "message": "..." }
```

with HTTP `403`.

### Admin CRUD

The admin API is opt-in. Mount it only with an admin authorizer:

```csharp
builder.Services.AddFunQueryNamedQueries(options =>
{
    options.ConnectionString = connectionString;
});

builder.Services.AddFunQueryNamedQueryAdmin(options =>
{
    options.Route = "/api/funquery/admin/named-queries";
});

app.MapFunQueryNamedQueryAdmin()
   .RequireAuthorization("FunQueryAdmin");
```

Default route recommendation: `/funquery/admin/named-queries`. Do not use a generic `/admin/named-queries`
if the Web API already owns `/admin`.

The admin service is also available as `INamedQueryAdminService` for an existing admin controller. In
that case, do not map the endpoints and let the host's own authorization and routing apply.

Endpoints:

| Method | Path | Purpose |
| ------ | ---- | ------- |
| `GET` | `.../named-queries?app=&tenant=` | list |
| `GET` | `.../named-queries/{key}?app=&tenant=` | read |
| `POST` | `.../named-queries` | create |
| `PUT` | `.../named-queries/{key}?app=&tenant=` | replace |
| `PATCH` | `.../named-queries/{key}?app=&tenant=` | partial update |
| `DELETE` | `.../named-queries/{key}?app=&tenant=` | disable; `?purge=true` to delete |

Updates use `If-Match: "<version>"` (or `version` in the body) and increment the catalog version.

### Keycloak / SSO

Authentication is owned by the host. Wire OIDC/JWT normally, then implement
`INamedQueryAccessProvider` and `INamedQueryAdminAuthorizer` with `IAuthorizationService` or claims.
See [`NamedQueryPlan.md`](NamedQueryPlan.md#authorization-and-sso) for an example.

## Host configuration

The PostgreSQL sample host reads:

```text
NamedQueries:Schema                 default funquery
NamedQueries:Table                  default named_queries
NamedQueries:EnableChangeListener   default true
FUNQUERY_POSTGRES                   catalog connection string
```

The host registers the store, lookup cache, `LISTEN/NOTIFY` listener, resolver, and admin service.
Admin endpoints are not mapped automatically.

## Errors

| Situation | Code | HTTP |
| --------- | ---- | ---- |
| Key not found | `UNKNOWN_NAMED_QUERY` | 404 |
| Key disabled | `NAMED_QUERY_DISABLED` | 404 |
| Caller not permitted | `NAMED_QUERY_FORBIDDEN` | 403 |
| `$named` not top-level | `INVALID_NAMED_QUERY_POSITION` | 400 |
| Key is empty | `INVALID_NAMED_QUERY_KEY` | 400 |
| `$named` argument is not a string | `TYPE_MISMATCH` | 400 |
| Catalog query invalid | `INVALID_NAMED_QUERY` | 500 |
| Catalog unavailable | `NAMED_QUERIES_UNAVAILABLE` | 503 |
| `key` plus `q`/`source` | `INVALID_REQUEST` | 400 |

## Limitations (v1)

* No nested `$named`.
* No `$named(...).$filter(...)` or `$source($named(...))`.
* The catalog table is not a query source.
* Direct `&min=100` parameters are not supported; use `v.min`.
* No cross-app/cross-tenant sharing.
* Admin CRUD has no UI; use the API or an existing admin controller.
* PostgreSQL only for the catalog store/listener.
