# FunQuery Providers

This document records the provider architecture decision for Milestone 6.

**Status:** PostgreSQL is the first implemented SQL provider; see [`PostgreSql.md`](PostgreSql.md).
Additional SQL dialects (SQL Server, MySQL, SQLite) are future work. The LINQ provider remains optional.

## Decision

* **One SQL provider, multiple dialects.** The query engine, plan, parameterization, null semantics, and
  capability checks are shared. A database is a small `ISqlDialect` implementation plus a connection
  factory, not a second query engine.
* **PostgreSQL is the first dialect.** It is the reference SQL target: real `date`, `numeric`, `boolean`,
  `NULLS FIRST/LAST`, binary collation (`"C"`), and window functions.
* **Direct SQL, not LINQ → SQL.** The SQL provider renders a provider-neutral relational plan to
  parameterized SQL. EF Core/LINQ is not used as the SQL translator.
* **LINQ is a separate provider**, for `IEnumerable`/`IQueryable` composition. It is not the path to SQL.
* **The plan IR is implemented** in `src/FunQuery.Sql` (M6.1): `PlanBuilder`, `PlanValidator`,
  `PlanSource`, `ProviderCapabilities`, and `IQueryExecutor`.

## Layers

```text
Annotated AST
      │
      ▼
RelationalPlan (provider-neutral)
      ├── InMemory provider        (existing interpreter)
      ├── LINQ provider            (separate, optional)
      └── SQL provider
            ├── SqlRenderer      (plan → SQL AST)
            ├── ISqlDialect      (PostgreSQL first)
            └── SqlExecutor      (DbConnection/DbCommand/parameters)
```

The plan IR is the contract. Everything above it is provider-neutral; everything database-specific lives
in the dialect and the connection factory.

## Project layout

```text
src/
  FunQuery.Sql/                 shared SQL provider: plan, SQL AST, renderer,
                                ISqlDialect, generic DbConnection executor
  FunQuery.PostgreSql/          PostgreSQL dialect + Npgsql source/executor
  FunQuery.Linq/                optional LINQ provider (IEnumerable/IQueryable)
hosts/
  FunQuery.Server/              references AspNetCore and the providers it enables
tests/
  FunQuery.Sql.Tests/           golden SQL tests, no database
  FunQuery.PostgreSql.Tests/    integration and parity tests
```

Rules:

* One class library per provider **family or driver**, not per query feature.
* `FunQuery.Sql` has no database driver dependency.
* `FunQuery.PostgreSql` is the only project that references `Npgsql`.
* `FunQuery.AspNetCore` references neither; the host wires the provider it wants.
* Future SQL Server/MySQL/SQLite packages contain only their dialect, connection factory, and type
  mapper. They reference `FunQuery.Sql`, not each other.
* The LINQ provider is a separate package and does not become the SQL translator.

## Shared across SQL dialects

| Concern | Where |
| ------- | ----- |
| Plan IR (`Scan`, `Filter`, `Project`, `Sort`, `Take`, `Skip`, `Distinct`, `Aggregate`, `Any`, `First`) | core |
| SQL AST and renderer | core SQL provider |
| Typed parameters and parameter names | renderer + dialect |
| Two-valued null rewriting | renderer |
| Capability validation | plan builder + provider |
| Command execution, async, cancellation, row materialization | generic `DbConnection`/`DbCommand` executor |
| Identifier safety | schema + dialect quoting; never from user text |

## Per-database work

A new database normally needs only:

1. `ISqlDialect`: identifier quoting, parameter names, pagination, type names, collation, null ordering.
2. A connection factory (`NpgsqlDataSource` for PostgreSQL, `DbProviderFactory` for others).
3. A type mapper where `DbType` is not enough.
4. Capability flags for what the database cannot guarantee.
5. Golden SQL tests + integration/parity tests.

It does **not** need a new parser, analyzer, plan builder, or result model.

## PostgreSQL mapping (v1)

| FunQuery | PostgreSQL | Notes |
| -------- | ---------- | ----- |
| `int` | `integer` | |
| `long` | `bigint` | |
| `decimal` | `numeric` | exact, no binary floating point |
| `string` | `text` | comparisons use `COLLATE "C"` |
| `bool` | `boolean` | |
| `date` | `date` | no time or time zone |
| `array<T>` | `jsonb` or `T[]` | filtering inside arrays is not supported in v1 |
| `object` | `jsonb` | projection only; filtering inside objects is not supported in v1 |

### Semantic rules

* **Null:** SQL is three-valued; FunQuery is two-valued. The renderer uses PostgreSQL
  `IS NOT DISTINCT FROM` for `eq` and `IS DISTINCT FROM` for `neq`, which matches FunQuery exactly
  (`null eq null` true, `null neq null` false, `null neq 5` true). Orderings keep SQL's behavior: a
  null operand is false. Boolean operands and `not` are wrapped with `COALESCE(..., FALSE)` so a null
  comparison is never unknown.
* **Strings:** comparisons and ordering use `COLLATE "C"` (binary, case-sensitive).
  `contains`/`startswith` use `strpos`; `endswith` uses `right` + `length` under the same collation.
* **Sorting:** `NULLS FIRST` for `asc`, `NULLS LAST` for `desc`, matching "null is the smallest value".
* **Pagination:** `LIMIT`/`OFFSET`. The provider requires a preceding `$sort`; without an explicit order,
  SQL row order is unspecified and the result would not be reproducible.
* **`$distinct(key)`:** `SELECT DISTINCT ON (key)`. The language does not require preserving "first
  occurrence" order in SQL; order after `$distinct` is unspecified unless followed by `$sort`.
* **`$index`:** `ROW_NUMBER() OVER (ORDER BY ...)`, requiring an explicit `$sort`. In a predicate it is
  computed in a derived table first, because PostgreSQL does not allow window functions in `WHERE`.
  `$index(base)` adds the base parameter to the zero-based position.
* **`$first` / `$any` / `$count`:** `LIMIT 1`, `EXISTS (...)`, `COUNT(*)`.
* **`$coalesce`:** `COALESCE(...)`.
* **`$select` / `$map`:** projection. Scalar projections are v1; object projections use
  `jsonb_build_object` only when the provider declares the `JsonProjection` capability.
* **Inline `$source([...])`:** rejected by the SQL provider (`UNSUPPORTED`) in v1. SQL reads registered
  sources only.
* **`$field(@variable)`:** the variable must resolve to a known schema field at plan time. A dynamic
  identifier is rejected; identifiers are never taken from raw user text.
* **`$let`:** resolved at plan time; the value becomes a typed parameter or constant.

### Known PostgreSQL limitations (declared as capabilities)

| Feature | Limitation |
| ------- | ---------- |
| `$lower` / `$upper` | PostgreSQL `lower`/`upper` follow the database collation; invariant non-ASCII casing is not guaranteed. v1 supports ASCII, or the provider declares the limitation |
| `$length` | `length()` counts Unicode code points, while FunQuery counts UTF-16 code units. Non-BMP characters differ (`'😀'`: 1 vs 2) |
| Arrays/objects in predicates | not supported in v1; `jsonb` projection only |
| Inline sources | not supported in v1 |
| Dynamic `$field` identifiers | rejected; only schema fields |
| `$take`/`$skip` without `$sort` | rejected; deterministic order required |

These are capability limitations, not silent behavior changes. A query that needs an unsupported feature
fails with a clear error; it never falls back to in-memory execution.

## Execution (M6.3)

`PostgreSqlSource` implements `IQuerySourceExecutor`:

```csharp
var source = new PostgreSqlSource(
    name: "orders",
    schema: ordersSchema,
    table: "orders",
    dataSource: npgsqlDataSource,
    commandTimeout: TimeSpan.FromSeconds(10));
```

* `Name`, `Schema`, and `Table` describe the source; `NpgsqlDataSource` is shared/pooled.
* `ExecuteAsync(QueryPlan, QueryEnvironment, CancellationToken)` builds the `RelationalPlan`, renders
  SQL, binds typed `NpgsqlParameter`s, and reads the result into `ObjectValue`/scalar values.
* Parameters map to `NpgsqlDbType`: `integer`, `bigint`, `numeric`, `text`, `boolean`, `date`.
* Object rows are materialized in schema field order; a missing result column reads as null.
* Command timeout and the request cancellation token are propagated to Npgsql.
* `MaxResultItems` is enforced while reading rows; the command timeout is the smaller of the source
  timeout and `QueryLimits.MaxExecutionTime`. `MaxSourceItems` is an in-memory loading limit and is not
  applied to a database source.
* The provider is read-only: it only issues the generated `SELECT` and never writes or runs DDL.

The HTTP adapter uses `IQuerySource.Executor` when present; otherwise it keeps the in-memory path.
`PostgreSqlSource` is wrapped with `AsQuerySource(...)` for the HTTP `SourceRegistry`, so the provider
does not reference ASP.NET Core.

Integration tests use `FUNQUERY_POSTGRES`:

```text
Host=127.0.0.1;Port=5433;Username=postgres;Password=postgres;Database=postgres
```

The sample `hosts/FunQuery.PostgreSql.Server` reads `PostgreSql:ConnectionString` or
`FUNQUERY_POSTGRES` and registers a PostgreSQL `orders` source plus an in-memory `people` source. Both
are served through the same GET/POST API. Provider `QueryException`s keep the normal `400`/`422`
mapping; Npgsql connection/command failures become a generic `500` and are logged without SQL text or
credentials.

The PostgreSQL host is published with Native AOT (`PublishAot`), and Npgsql 10.0.3 worked in the AOT
smoke test for GET, POST, and the in-memory source. The in-memory host remains independently
AOT-published.

## Parity (M6.4)

The integration suite runs the same `QueryPlan` through the in-memory interpreter and `PostgreSqlSource`
and compares the JSON. The covered subset includes filtering, projection, sorting, pagination,
`$count`/`$any`/`$first`, `$index`, strings, dates, nulls, `$coalesce`, and `$distinct`. Unsupported
features fail with a stable code rather than a different result.

## Security

* Values are always typed parameters (`NpgsqlParameter`); no string concatenation of user values.
* Identifiers come from the registered schema, quoted by the dialect; never from raw input.
* Queries are read-only; the provider does not expose writes, transactions, or multiple statements.
* Connection strings and server details never appear in client errors.
* Command timeout and `CancellationToken` are propagated from the HTTP request.

## Testing strategy

1. **Golden SQL tests** (no database): plan → expected SQL text + parameters for each dialect.
2. **Integration tests** against PostgreSQL (Testcontainers or `FUNQUERY_POSTGRES` environment variable).
3. **Parity tests**: the same query and data produce the same JSON in-memory and in PostgreSQL.
4. **Capability tests**: unsupported queries fail with the documented code instead of returning a
   different result.

## Non-goals for M6

* Writes, DDL, transactions, stored procedures.
* Arbitrary SQL, `SELECT *`, or dynamic identifiers.
* Full-text search, window functions beyond `$index`, JSON path filtering.
* Multiple statements per query.
* A LINQ-to-SQL translation layer.
