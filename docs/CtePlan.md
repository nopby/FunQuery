# CTE Support for `$let` — Work Plan

**Status:** 📝 Proposed. Not implemented.

**Goal:** let a `$let` binding hold a **result set**, and let the PostgreSQL provider render that
binding as a CTE so `$source(@name)` can read it. The language gains no new function: `$let` remains the
single declaration mechanism for both scalar values and result sets.

```text
$let(@recent, $source(@source).$filter(ordered_on gt $date('2024-01-01')))
    .$source(@recent)
    .$filter(total gt 100)
    .$select(id, customer, total)
```

Expected SQL:

```sql
WITH "fq_recent" AS (
    SELECT * FROM "orders" WHERE "ordered_on" > @p0
)
SELECT "id", "customer", "total"
FROM "fq_recent"
WHERE "total" > @p1
```

## Decisions

1. **Extend `$let`; do not add `$cte` or `$with`.** A second syntax would duplicate `$let` and split the
   language.
2. **PostgreSQL first.** The in-memory interpreter already supports result-set `$let`; it is the semantic
   reference.
3. **Read-only.** The feature still produces one parameterized `SELECT`. No temp tables, DDL, writes, or
   multiple statements.
4. **No new language syntax.** The analyzer already accepts array values for `$let` and validates
   `$source(@name)` against the array type.
5. **Object-shaped result sets only in v1.** The CTE element type must be an `object` (the same shape a
   registered source exposes). Scalar arrays stay in-memory only and are rejected by SQL with
   `NOT_SUPPORTED`.

## Non-goals

* `$with` / `$cte` aliases.
* Recursive CTEs (`WITH RECURSIVE`).
* Temp tables or session-scoped state.
* CTE materialization hints (`MATERIALIZED` / `NOT MATERIALIZED`).
* Other SQL dialects (SQL Server, MySQL, SQLite).
* DML, DDL, or cross-request persistence.
* Inline arrays as CTE bodies in the SQL provider (`$source([...])` remains `NOT_SUPPORTED`).

## Semantics

`$let(@name, value)` keeps its current rules:

* the first argument is a bare declaration target; the value is evaluated once, without a current row;
* `$let` is transparent to the chain: it passes its target through unchanged;
* scope grows forward only; redeclaration in the same scope is `VARIABLE_REDEFINED`;
* a nested `$let` value has its own scope; its bindings do not leak.

The new rule is only about the **value type**:

* scalar value → the existing `PlanExpression` binding (literal or parameter);
* `array<object>` value → a relational binding and a CTE definition;
* `$source(@name)` resolves a relational binding to a CTE scan;
* `$source(@name)` on a scalar binding is a type error, as it already is in the analyzer;
* a CTE may reference an earlier scalar binding (as a parameter) and an earlier CTE;
* a CTE is defined before it is used, matching query order;
* the same CTE may be scanned more than once; the definition is emitted once.

The in-memory interpreter is unchanged and must remain the parity target: `$let` computes the value
eagerly, and `$source(@name)` iterates the materialized array.

## Design

### 1. Plan IR

Add provider-neutral nodes in `src/FunQuery.Sql/Plan/RelationalPlan.cs`:

```csharp
public sealed record CteDefinition(
    string VariableName,
    string SqlName,
    ObjectType Schema,
    RelationalPlan Plan);

public sealed record With(
    IReadOnlyList<CteDefinition> Ctes,
    RelationalPlan Body) : RelationalPlan;

public sealed record CteScan(
    string SqlName,
    ObjectType Schema) : RelationalPlan;
```

`CteScan` is deliberately separate from `Scan`: a scan is a registered table, a CTE scan is a
query-local object and must not consult the host's `SqlTable` mapping.

### 2. PlanBuilder

Change `_variables` from `Dictionary<string, PlanExpression>` to a binding union:

```csharp
private abstract record PlanBinding;
private sealed record ScalarBinding(PlanExpression Expression) : PlanBinding;
private sealed record RelationalBinding(RelationalPlan Plan, ObjectType Schema) : PlanBinding;
```

* `ApplyLet` branches on the value's semantic type:
  * scalar → `ScalarBinding(BuildExpression(value))`;
  * `ArrayType { Type: ObjectType schema }` → `RelationalBinding(BuildRelational(value), schema)` and
    append a `CteDefinition` to the current scope;
  * other arrays → `NOT_SUPPORTED` for the SQL provider.
* `BuildRelational(value)` accepts a function chain (the normal case) and returns its plan. The CTE
  list is saved/restored around the value so nested `$let` declarations remain local to that value.
* `BuildSourceVariable` resolution order:
  1. registered source from `_sources` → `Scan`;
  2. relational binding → `CteScan`;
  3. otherwise → the existing `NOT_SUPPORTED` error, with a clearer message for scalar bindings.
* `EnterScope` snapshots both bindings **and** the current CTE list.
* `Build(...)` wraps the resulting plan in `With` when the root scope declared CTEs.
* CTE SQL names use a deterministic prefix derived from the variable name, for example `fq_recent`.
  The name is always quoted by the dialect; the prefix avoids collisions with real table names.

### 3. SQL layer

* Add `SqlCte(string Name, SqlQuery Query)`.
* Add an init-only `Ctes` property to `SqlQuery` so existing constructors do not change:
  ```csharp
  public IReadOnlyList<SqlCte> Ctes { get; init; } = [];
  ```
* Add `SqlCteSource(string Name)` to the `SqlSource` hierarchy.
* `SqlWriter.Write` emits `WITH name AS (...)` before `SELECT` when `Ctes` is non-empty.
* `SqlRenderer.BuildPlan` handles `With` and `CteScan`.
  * `With` builds each CTE body and attaches the definitions to the body query.
  * `CteScan` returns `SELECT * FROM "fq_name"`.
* `PlanValidator` validates `With` and `CteScan`.
* Add `ProviderCapabilities.Cte` (`1 << 12`) and include it in `ProviderCapabilities.All`.
* `PostgreSqlDialect.Capabilities` adds `Cte`. A provider without the flag returns `NOT_SUPPORTED`
  before touching the database.

### 4. Column shape

The CTE body must expose columns in the same order as the `ObjectType` schema from the semantic type.
This is already how registered sources and `$select` projections work; the CTE scan uses `SELECT *` so
the order must be preserved.

If the CTE body has a scalar projection (no object key), it is not a valid v1 CTE source. Reject it with
`NOT_SUPPORTED` rather than inventing a column name.

## Stages

| Stage | Name | Depends on | Size | Status |
| ----- | ---- | ---------- | ---- | ------ |
| C1 | Semantics and in-memory regression tests | — | S | ⏳ |
| C2 | Plan IR and `PlanBuilder` bindings | C1 | M | ⏳ |
| C3 | SQL AST, renderer, validator, capability | C2 | M | ⏳ |
| C4 | PostgreSQL and HTTP integration tests | C3 | S | ⏳ |
| C5 | Documentation, AOT smoke, close-out | C1–C4 | S | ⏳ |

### C1 — Semantics and in-memory regression tests

* Confirm `$let` accepts `array<object>` values and that `$source(@name)` works in memory.
* Add tests for: single use, repeated use, multiple bindings, scalar + result set in one chain, null
  fields, empty result, nested `$let` scoping, and redeclaration errors.
* Do not change the interpreter unless a test exposes a gap.

### C2 — Plan IR and `PlanBuilder` bindings

* Add `With`, `CteDefinition`, and `CteScan`.
* Replace `_variables` with scalar/relational bindings.
* Extend scope snapshots to include CTEs.
* Resolve `$source(@name)` to CTEs.
* Add plan tests that assert the nodes and declaration order.
* Reject scalar-array and inline-array CTE bodies with `NOT_SUPPORTED` (v1 limitation).

### C3 — SQL AST, renderer, validator, capability

* Add `SqlCte`, `SqlCteSource`, and `SqlQuery.Ctes`.
* Emit `WITH` in `SqlWriter`.
* Handle `With`/`CteScan` in `SqlRenderer`.
* Add `ProviderCapabilities.Cte` and validate it.
* Add golden SQL tests for one CTE, multiple CTEs, repeated scans, parameters inside a CTE, filtering,
  sorting, and pagination outside the CTE.

### C4 — PostgreSQL and HTTP integration tests

* Add live PostgreSQL tests comparing in-memory and SQL results for the same query.
* Cover a CTE with a variable (`@min`) and a CTE read twice.
* Cover an empty CTE, null values, and date filtering.
* Cover POST `/query` through the sample host.
* Confirm no write/DDL statement can be produced.

### C5 — Documentation, AOT smoke, close-out

* Document result-set `$let` and its CTE rendering in `Variables.md`.
* Add CTE support and limitations to `PostgreSql.md` and `Providers.md`.
* Add the CTE capability to the supported-subset table.
* Record the final `dotnet test` count and Native AOT publish/smoke result.
* Update this plan with a Result section when complete.

## Tests

Plan and renderer tests must not need a database. Live tests run only when `FUNQUERY_POSTGRES` is set,
matching the existing PostgreSQL test convention.

Required cases:

| Case | Expected |
| ---- | -------- |
| Scalar `$let` | unchanged literal/parameter behavior |
| Result-set `$let` | `With` containing one CTE |
| `$source(@x)` after CTE | `CteScan` |
| Two `$source(@x)` uses | one `CteDefinition`, two scans |
| Two CTEs, second references first | ordered `WITH` clauses |
| CTE body uses `@min` | parameterized predicate in the CTE |
| Outer filter after CTE | predicate outside the CTE |
| CTE with empty result | empty result, no error |
| Scalar array CTE | `NOT_SUPPORTED` |
| Inline array CTE | `NOT_SUPPORTED` |
| Provider without `Cte` | `NOT_SUPPORTED` before execution |
| In-memory parity | same rows and order as SQL |
| HTTP POST with CTE | same result as GET/in-memory |

## Definition of Done

* `$let(@name, <object result set>)` plus `$source(@name)` works against PostgreSQL.
* The generated SQL uses `WITH` and typed parameters; values are never concatenated into SQL.
* Results match the in-memory provider for the covered subset.
* A provider without the CTE capability rejects the plan with `NOT_SUPPORTED`.
* No temp tables, DDL, or writes are introduced.
* Documentation is updated and the full test suite, PostgreSQL integration tests, and Native AOT smoke
  test pass.

## Risks and open decisions

1. **Nested CTE scope.** The plan uses save/restore of the CTE list to keep nested `$let` values local.
   If this proves too complex, v1 can reject result-set `$let` outside the top-level chain and add
   nested support later.
2. **CTE naming.** Prefixing (`fq_<name>`) avoids collisions with real tables; the trade-off is less
   readable generated SQL. Deterministic quoting is mandatory.
3. **Materialization.** PostgreSQL may inline a CTE. This does not change the language result. A future
   `MATERIALIZED` hint can be added without changing `$let` syntax.
4. **Column order.** The CTE body's projection order must match the semantic object schema. Tests must
   assert this explicitly.
5. **Registered source or binding first?** `$source(@source)` remains the registered source; result-set
   bindings use their own variable names. The analyzer keeps names unique per visible scope, so the plan
   builder should not need a fallback that silently changes meaning.
6. **Scalar arrays.** Supporting `array<int>` CTEs would require a single synthetic column and a
   follow-up language decision; keep them out of v1.
