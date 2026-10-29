# Milestone 6 — Providers: Work Plan (PostgreSQL first)

This document expands [Milestone 6](Milestones.md#milestone-6--providers) into stages. The architecture
decision is recorded in [`Providers.md`](Providers.md): one shared SQL provider, a small dialect per
database, and PostgreSQL as the first dialect.

Every stage must leave the solution building without warnings, with its tests passing, before the next
one starts.

## Principles

* **One plan, many executors.** The annotated AST is lowered to a provider-neutral `RelationalPlan`.
  In-memory, LINQ, and SQL consume the same plan.
* **Dialects are small.** A database is a dialect + connection factory + type mapper, not a second query
  engine.
* **Capabilities are explicit.** A provider rejects what it cannot translate, at plan time, with a stable
  error code. It never falls back to in-memory execution.
* **Semantics are the reference.** In-memory remains the reference; a SQL query must produce the same
  JSON for the supported subset.
* **Parameters, never text.** Values are typed parameters. Identifiers come from the schema and are quoted
  by the dialect.

## Stage overview

| Stage | Name                                      | Depends on | Size | Status |
| ----- | ----------------------------------------- | ---------- | ---- | ------ |
| M6.0a | Query playground                           | —          | S    | ✅     |
| M6.1  | Provider contract and relational plan      | —          | L    | ✅     |
| M6.2  | SQL core and PostgreSQL dialect            | M6.1       | L    | ⏳     |
| M6.3  | PostgreSQL execution and sources           | M6.2       | L    | ⏳     |
| M6.4  | Semantic parity and capability enforcement | M6.3       | M    | ⏳     |
| M6.5  | HTTP integration and hardening             | M6.4       | M    | ⏳     |
| M6.6  | Documentation and close-out                | M6.1–M6.5  | S    | ⏳     |

---

## M6.0a — Query playground

**Goal:** a browser page for trying queries against the sample host without `curl`.

### Work

* `hosts/FunQuery.Server/wwwroot/playground/`: self-contained HTML, CSS, and JS. No CDN, no inline
  scripts or styles.
* Query editor, source input with a datalist, variables JSON editor, examples, history in
  `localStorage`, `Ctrl`+`Enter` to run.
* Uses `POST /query`, so long queries and typed variables work exactly as in the API.
* `GET /playground/config` returns examples and source names, but only when
  `Playground:ExposeSourceNames` is true; otherwise sources are empty and source examples are omitted.
* `PlaygroundOptions` (`Enabled`, `ExposeSourceNames`, `SourceNames`, `Examples`) is host-owned and
  overridable in tests.
* Security headers for `/playground`: CSP (`'self'` only), `X-Content-Type-Options`, `X-Frame-Options`,
  `Referrer-Policy`.
* The playground is opt-in: when `Enabled` is false, the page, static assets, and config endpoint are
  not mapped.

### Tests

* Page, JS, and CSS are served when enabled; `/playground` redirects to `/playground/`.
* Config lists sources and examples when allowed.
* Source names and source examples are hidden when `ExposeSourceNames` is false.
* The page and config return `404` when disabled.
* CSP and the other security headers are present.
* The HTML contains no external `http://` or `https://` resources.

### Definition of Done

* `dotnet run` serves `/playground/`; the page can run a query and show the JSON result or the
  structured error.
* AOT publish includes `wwwroot` and the config endpoint works when the binary runs from its publish
  directory.
* Source names are never exposed unless the host explicitly enables it.

---

## M6.1 — Provider contract and relational plan

**Goal:** lower the annotated AST to a provider-neutral relational plan, without touching a database.

### Work

* New `src/FunQuery.Sql` (no driver dependency).
* `RelationalPlan` nodes:
  * `Scan` (registered source), `Values` (inline source, SQL may reject it later);
  * `Filter`, `Project`, `Sort`, `Take`, `Skip`, `Distinct`, `Aggregate`, `Any`, `First`;
  * expressions: `Column`, `Literal`, `Parameter`, `Binary`, `Logical`, `Not`, `ScalarCall`,
    `ObjectConstruct`, `ArrayConstruct`.
* `PlanBuilder`: annotated AST → plan. It:
  * substitutes `$let` values and environment parameters;
  * turns `$source(@source)` into `Scan` and inline `$source([...])` into `Values`;
  * maps `$select`/`$map` to `Project`;
  * keeps `$index` and `$distinct` as explicit plan nodes, not dialect tricks.
* `ProviderCapabilities` flags: filtering, sorting, projection, pagination, string operations, date
  operations, aggregation, distinct-on, window functions, JSON projection, inline sources.
* `IQueryExecutor` contract: plan + environment + cancellation → runtime value.
* The in-memory interpreter stays the reference; it may be adapted to consume the plan later, but M6.1
  must not change its behavior.

### Tests

* Plan shape for `$filter`, `$select`, `$map`, `$sort`, `$take`, `$skip`, `$count`, `$any`, `$first`,
  `$distinct`, `$index`.
* `$let` and request variables become `Parameter`/`Literal`, not unresolved names.
* Inline source becomes `Values`; registered source becomes `Scan`.
* Capability checks reject what a provider declares unsupported.
* Existing 1055 tests still pass.

### Definition of Done

* The plan can be built for every v1 function and scalar expression.
* No PostgreSQL/Npgsql dependency in `FunQuery.Sql`.
* The plan is deterministic and testable without a database.

### Result

* `src/FunQuery.Sql` contains the plan IR, `PlanBuilder`, `PlanValidator`, `PlanSource`,
  `ProviderCapabilities`, and `IQueryExecutor`.
* `tests/FunQuery.Sql.Tests` covers plan shapes and capability rejection (29 tests); no driver package
  is referenced.
* Literal parsing moved to the shared `LiteralValues` helper, used by both the in-memory compiler and
  the plan builder, so the two cannot drift.

---

## M6.2 — SQL core and PostgreSQL dialect

**Goal:** render the relational plan to parameterized PostgreSQL SQL.

### Work

* `FunQuery.Sql`:
  * SQL AST (`Select`, `From`, `Where`, `OrderBy`, `Limit/Offset`, `DistinctOn`, `Exists`, …);
  * `SqlRenderer` with two-valued null rewriting;
  * `SqlParameter` collection and typed value model;
  * `ISqlDialect` (quoting, parameter names, pagination, type names, collation, null ordering).
* `FunQuery.PostgreSql`:
  * `PostgreSqlDialect`:
    * `"identifier"` quoting;
    * `@p0` parameters;
    * `integer`/`bigint`/`numeric`/`text`/`boolean`/`date`;
    * `LIMIT`/`OFFSET`;
    * `NULLS FIRST/LAST`;
    * `COLLATE "C"` for string comparisons;
  * capability flags, including the known limitations from [`Providers.md`](Providers.md).
* Null rewriting:
  * `eq null` / `neq null` → `IS NULL` / `IS NOT NULL`;
  * `neq` with a value → `(x <> @p OR x IS NULL)`;
  * boolean operands and `not` wrapped so null is false, never unknown.
* Pagination requires a preceding `$sort`; otherwise the renderer fails with a clear code.

### Tests

* Golden SQL for each plan node: exact SQL text and parameters, no database.
* Null cases: `null eq 5`, `null neq 5`, `x neq @p`, `not (x eq 5)`, `and`/`or` with nulls.
* String comparisons carry `COLLATE "C"`.
* `$sort` renders `NULLS FIRST`/`NULLS LAST`.
* `$take`/`$skip` without `$sort` is rejected.
* Unsupported features (`$lower` non-ASCII, inline source, dynamic `$field`) fail explicitly.

### Definition of Done

* Every supported v1 function has a golden SQL test.
* The renderer never concatenates a user value into SQL.
* PostgreSQL-specific knowledge is confined to `FunQuery.PostgreSql`.

---

## M6.3 — PostgreSQL execution and sources

**Goal:** execute a plan against a real PostgreSQL database through a registered source.

### Work

* `FunQuery.PostgreSql`:
  * `PostgreSqlSource`: name, schema, table (or view), capabilities, connection descriptor;
  * `NpgsqlDataSource` connection factory (pooled);
  * async command execution, typed `NpgsqlParameter`s, `CancellationToken`, command timeout;
  * row materialization into `ObjectValue` using the declared schema;
  * DI extension (`AddFunQueryPostgreSql(...)`).
* Provider selection:
  * a source declares its provider kind;
  * the adapter resolves the matching `IQueryExecutor` (in-memory or PostgreSQL);
  * unknown/unsupported combinations fail with a clear error.
* The SQL provider is read-only: no writes, no DDL, no multiple statements.

### Tests

* Integration against PostgreSQL (Testcontainers or `FUNQUERY_POSTGRES` environment variable).
* `$source(@source).$filter(...).$select(...)` returns the expected JSON.
* Parameters are typed; a value with a quote or newline is handled safely.
* Cancellation and command timeout propagate.
* Schema mismatch and unknown field fail before executing SQL.

### Definition of Done

* A registered PostgreSQL source runs through the HTTP host.
* The in-memory path still works unchanged.
* No connection string or SQL text appears in client errors.

---

## M6.4 — Semantic parity and capability enforcement

**Goal:** prove that the supported subset produces the same results in memory and in PostgreSQL.

### Work

* Dates: `date` ↔ `date`, `$date` parameters, comparisons and sorting.
* Strings: binary collation, `contains`/`startswith`/`endswith`, null behavior.
* `$coalesce`, `$first`, `$any`, `$count`.
* `$distinct(key)` via `DISTINCT ON`; order after `$distinct` documented as unspecified.
* `$index` via `ROW_NUMBER() OVER (ORDER BY ...)`, requiring `$sort`.
* Capability enforcement for known gaps:
  * invariant `$lower`/`$upper` (non-ASCII);
  * `$length` UTF-16 vs code points;
  * arrays/objects in predicates;
  * inline sources;
  * dynamic `$field` identifiers;
  * pagination without `$sort`.
* Parity suite: the same query and data produce identical JSON in both providers.

### Tests

* Parity for filter/project/sort/take/skip/count/any/first/distinct/coalesce/date/string.
* Null parity: two-valued comparisons, `neq`, `not`, `and`/`or`.
* Capability errors for each documented limitation.
* Golden SQL for the newly supported functions.

### Definition of Done

* Every query in the supported subset has a parity test.
* Unsupported features fail with a stable code, never a different result.

---

## M6.5 — HTTP integration and hardening

**Goal:** make the PostgreSQL provider usable through the existing GET/POST endpoints.

### Work

* Host wiring: `AddFunQueryPostgreSql(...)` + a PostgreSQL source registered in `SourceRegistry`.
* Provider-aware execution in the adapter; in-memory sources keep working.
* Error mapping for provider errors:
  * `UNSUPPORTED` / capability errors → `400` or `422` as documented;
  * connection/command errors → `500` with a generic message, details only in the log.
* Limits: command timeout, cancellation, result size, source size.
* Concurrency: parallel requests with different sources and parameters.
* AOT assessment for Npgsql; document whether the SQL host is AOT-published or not.

### Tests

* GET and POST against a PostgreSQL source.
* Error envelope parity with the in-memory provider.
* Cancellation and timeout.
* Concurrent requests do not share connections or parameters.
* No credentials or SQL text in responses.

### Definition of Done

* The sample host can serve both in-memory and PostgreSQL sources.
* The security checklist from M5 still holds.

---

## M6.6 — Documentation and close-out

**Goal:** make the provider usable without reading the source, and declare M6 complete.

### Work

* `docs/PostgreSql.md`: source registration, schema mapping, capabilities, limitations, examples.
* Update [`Providers.md`](Providers.md) with the final implementation notes.
* README: PostgreSQL quickstart and M6 marked ✅.
* Record the supported/unsupported matrix.
* Final `dotnet test` and a PostgreSQL integration run.

### Definition of Done

* All Milestone 6 DoD items in [Milestones.md](Milestones.md#milestone-6--providers) are satisfied for
  PostgreSQL.
* The supported subset is documented and covered by parity tests.
* A user can register a PostgreSQL source and query it from the HTTP host.

---

## Open decisions

1. **Source/provider selection shape.** Whether `IQuerySource` gains a provider key, or a separate
   `ISqlSource`/`IQueryExecutor` registry is used. M6.1 decides; M6.3 implements.
2. **Pagination without `$sort`.** Proposed: reject. Alternative: order by a schema-declared key.
3. **`$distinct(key)` row choice.** Proposed: `DISTINCT ON` with unspecified order, documented.
4. **JSON projection.** Whether `$select({...})` maps to `jsonb_build_object` in v1 or is a capability
   limitation until later.
5. **Npgsql AOT.** Whether the SQL host is AOT-published; Npgsql support is assessed in M6.5.
