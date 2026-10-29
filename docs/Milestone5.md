# Milestone 5 — ASP.NET Core Integration: Work Plan

This document expands [Milestone 5](Milestones.md#milestone-5-aspnet-core-integration) into
independently shippable stages. Every stage must leave the solution building without warnings, with its
tests passing, before the next one starts.

The HTTP adapter is a **host**, not part of the language. `FunQuery` (the core project) must never
reference ASP.NET Core types; it only gains a small, provider-neutral way to receive pre-bound parameters
and per-request limits.

## Principles

* **The language stays independent.** The adapter owns HTTP concerns: routing, content types, status
  codes, request size, and source resolution.
* **Explicit registration.** A query can only reach a source that was registered at startup. No
  reflection-based access to application objects.
* **Schema before data.** The analyzer sees the source's declared fields and types, so an unknown field or
  a wrong comparison fails during analysis, not halfway through execution.
* **AOT-safe.** No reflection-based JSON serialization. Request/response DTOs use a
  `JsonSerializerContext`; query results keep using `ValueJson`.
* **Limits are part of the contract.** Time, result size, input size, token count, depth, and parameter
  count are enforced by the server, not left to the caller.

## Target layout

```text
FunQuery.slnx
├── FunQuery.csproj                     core language + in-memory interpreter (no HTTP)
├── src/FunQuery.AspNetCore/            adapter: DI, endpoints, errors, source registry
├── hosts/FunQuery.Server/              runnable sample host + sample sources
└── tests/
    ├── FunQuery.Tests/                 existing unit tests
    └── FunQuery.AspNetCore.Tests/      integration tests (WebApplicationFactory)
```

`Directory.Build.props` currently excludes only `tests/**` from the root project's `**/*.cs` glob. The
root project must also exclude `src/**` and `hosts/**`, otherwise the adapter and host sources are compiled
into `FunQuery.dll`.

## Stage overview

| Stage | Name                                   | Depends on | Size | Status |
| ----- | -------------------------------------- | ---------- | ---- | ------ |
| M5.1  | Host skeleton and GET pipeline          | —          | M    | ✅     |
| M5.2  | Structured errors and status mapping    | M5.1       | S    | ✅     |
| M5.3  | Typed parameters, source registry, schema | M5.1     | L    | ✅     |
| M5.4  | Query variables (GET)                   | M5.3       | M    | ✅     |
| M5.5  | POST endpoint and request contract      | M5.4       | S    | ✅     |
| M5.6  | Execution limits and cancellation       | M5.3       | M    | ✅     |
| M5.7  | Hardening, docs, and milestone close-out | M5.1–M5.6 | M    | ✅     |

---

## M5.1 — Host skeleton and GET pipeline

**Goal:** a runnable ASP.NET Core host that executes a FunQuery query from the query string and returns
its JSON result.

### Work

* Create `src/FunQuery.AspNetCore` (class library, `net10.0`, references `FunQuery.csproj`).
* Create `hosts/FunQuery.Server` (ASP.NET Core minimal API, references the adapter).
* Add both projects to `FunQuery.slnx`.
* Extend `Directory.Build.props` with `DefaultItemExcludes` for `src/**` and `hosts/**`.
* Adapter surface:
  * `AddFunQuery()` / `UseFunQuery()` (or `MapFunQueryEndpoints()`).
  * `FunQueryOptions` (route prefix, limits, JSON options).
* Endpoint `GET /query?q=<query>`:
  * validate `q` is present;
  * execute through `QueryEngine`;
  * return the result with `Content-Type: application/json; charset=utf-8`.
* Add a `JsonSerializerContext` for the adapter's DTOs (AOT).
* Keep `Program.cs` at the repository root (the REPL) unchanged.

### Tests

* `GET /query?q=$source([1,2,3]).$count()` returns `200` and `3`.
* A query with a JSON result returns the exact `ValueJson` output.
* A missing `q` returns `400` (final error shape lands in M5.2).
* The adapter project has no dependency on the test project, and the core has no ASP.NET reference.

### Definition of Done

* `dotnet run --project hosts/FunQuery.Server` serves the endpoint.
* The same call works from a browser and from `curl`.
* Solution builds with 0 warnings; existing 926 tests still pass.

---

## M5.2 — Structured errors and status mapping

**Goal:** every failure is a stable JSON document, not an HTML error page or a stack trace.

### Work

* Error DTO:
  ```json
  {
    "code": "TYPE_MISMATCH",
    "message": "Cannot compare string with int.",
    "position": 17,
    "length": 3
  }
  ```
  `position` and `length` are omitted when unknown (`-1`).
* Map `QueryException` to the envelope using `QueryException.CodeName` (the stable string contract).
* Status mapping (proposed; confirm before implementing):

  | Situation                                              | Status |
  | ------------------------------------------------------ | ------ |
  | Lexer, parser, analyzer, and runtime input errors      | `400`  |
  | Query string or request body too large                 | `413`  |
  | Too many tokens / depth exceeded                       | `400`  |
  | Execution timeout                                      | `408`  |
  | Result or source exceeds its item limit                | `422`  |
  | Unknown source / invalid parameter                     | `400`  |
  | Internal error (`IsInternal`)                          | `500`  |

* Internal errors return a generic message; the real message and stack go to the server log only.
* Catch-all error path: the endpoint filter catches anything the handler throws. An unexpected exception
  becomes `500` with `INTERNAL_ERROR`, never a stack trace.
* One status mapping, `FunQueryStatusCodes.For(QueryErrorCode)`, so the table and the implementation cannot
  drift.

### Tests

* Parse error: `400`, code `UNEXPECTED_TOKEN`, correct position.
* Unknown function: `400`, code `UNKNOWN_FUNCTION`.
* Internal error path: `500`, generic message, no stack trace in the body.
* `Content-Type` is `application/json` on every error.

### Definition of Done

* No response can leak an exception type, stack trace, or file path.
* The error schema is documented in `docs/HttpApi.md` (started here, finished in M5.7).

---

## M5.3 — Typed parameters, source registry, and schema

**Goal:** execute `$source(@source)` against a whitelisted source whose fields and types are known before
the query runs.

This is the largest stage: it adds the only new core API in M5 and the security boundary for data access.

### Core changes (`FunQuery`)

* `QueryParameter`: a parameter's semantic type plus its runtime value.
* `QueryEnvironment`: a read-only map of name → `QueryParameter`.
* `QueryEngine.Execute(string query, QueryEnvironment? environment = null)`:
  * seed `SemanticContext` with each parameter's type;
  * pass each parameter's value to `InMemoryCompiler`;
  * keep the existing `Execute(string)` overload as `Execute(query, QueryEnvironment.Empty)`.
* `QueryEngine.Prepare` / `ExecutePlan`: analysis happens against parameter types before values are
  needed, so a host can validate the query against a source schema and only then load rows.
* `InMemoryInterpreter.Execute(...)` accepts the runtime values and forwards them to the compiler.
* Row materialization helper: build `ObjectValue` rows from an ordered schema, rejecting rows whose
  values do not match the declared types (`TYPE_MISMATCH` with the row/field in the message).

### Adapter changes (`FunQuery.AspNetCore`)

* `IQuerySource`:
  * `Name`;
  * `ObjectType Schema`;
  * `ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(CancellationToken)`.
* `SourceRegistry`:
  * `Add(IQuerySource)`, duplicate name rejected at startup;
  * `TryGet(name)`, `Freeze()`, same pattern as `FunctionRegistry`;
  * no enumeration endpoint, so clients cannot discover source names.
* `GET /query?q=...&source=<name>`:
  * resolve the name; unknown name → `UNKNOWN_SOURCE` (`400`);
  * analyze the query against the schema with `Prepare` before loading anything;
  * load the rows (eagerly in v1), enforce `MaxSourceItems`;
  * bind the resolved data to the variable `@source` with type `array<schema>`;
  * execute the prepared plan.
* Sample host registers one or two in-memory sources (for example `orders` and `people`).

### Semantics

* `$source(@source)` is the only way to reach a registered source; `$source([...])` remains valid for
  inline data.
* The analyzer resolves fields from the declared schema, so `$filter(unknownField eq 1)` is
  `UNKNOWN_IDENTIFIER` and `$filter(id eq 'x')` is `TYPE_MISMATCH` before any row is read.
* A source parameter cannot be shadowed by `$let` (`VARIABLE_REDEFINED`).
* Capabilities are declared per source (`filtering`, `sorting`, `projection`, `pagination`, string/date
  operations, aggregation) and stored for M6. The in-memory adapter ignores them because it supports
  everything.

### Tests

* Registered source: `$source(@source).$filter(id gt 1).$select(id)` returns the expected rows.
* Unknown source → `400 UNKNOWN_SOURCE`.
* Unknown field → `400 UNKNOWN_IDENTIFIER` before execution.
* Schema type mismatch → `400 TYPE_MISMATCH`.
* A row that does not match the schema is rejected, not silently coerced.
* Duplicate source registration fails at startup.
* Inline `$source([...])` still works with no registry.

### Definition of Done

* No way to reach a data source that was not registered.
* Field and type errors are reported during analysis with a source position.
* Source loading is cancellation-aware.

---

## M5.4 — Query variables from the query string

**Goal:** supply `@variables` from the request, with types the analyzer can use.

### Work

* GET syntax: every parameter named `v.<name>` becomes `@name`:
  ```http
  GET /query?q=$source(@source).$filter(id gt @min)&source=orders&v.min=100
  ```
  * reserved query parameters: `q` and `source`; the `v.` prefix is reserved for variables;
  * the variable name `source` is reserved for the selected source;
  * value parsing: try a JSON scalar (`true`, `false`, `null`, number, quoted string); otherwise treat
    the raw text as a string;
  * dates are not a JSON scalar type, so use `$date(@raw)` in the query or POST in M5.5.
* Validate parameter names against the identifier rules; reject duplicates, `v.` with an empty name, and
  the reserved `source` name (`INVALID_PARAMETER`).
* Enforce `MaxParameters` (default 32) and `MaxParameterValueLength` (default 1024).
* Seed the analyzer and the compiler through `QueryEnvironment` (M5.3). No second binding mechanism.
* `$let` may not redefine an externally supplied parameter (`VARIABLE_REDEFINED`), matching the current
  variable rule.
* Values are read-only for the duration of the request; a `$let` inside the query cannot change them.

### Tests

* `v.min=100` binds an `int` usable in `id gt @min`.
* `v.name=Ana` binds a string; `v.active=true` binds a bool; `v.missing=null` binds null.
* Quoted JSON `v.name="Ana"` and bare `v.name=Ana` both bind `Ana`.
* Undefined `@x` → `400 UNDEFINED_VARIABLE`.
* Invalid name, duplicate name, too many parameters → `400 INVALID_PARAMETER`.
* JSON array/object and out-of-range numbers → `400 INVALID_PARAMETER`.
* An empty value `v.x=` binds an empty string.
* A `$let(@min, ...)` that collides with a request parameter → `400 VARIABLE_REDEFINED`.
* Variables are not leaked from one request to another under concurrency.

### Definition of Done

* Variables are typed, bounded, and validated; no value is evaluated as code.
* The GET contract is documented with examples.

---

## M5.5 — POST endpoint and request contract

**Goal:** accept larger queries and naturally typed variables in a JSON body.

### Work

* `POST /query` with:
  ```json
  {
    "query": "$source(@source).$filter(id gt @min).$take(10)",
    "source": "orders",
    "variables": { "min": 100, "region": "EU", "since": "2024-01-01" }
  }
  ```
* Same pipeline and response shape as GET; only how the query, source, and variables are read differs.
* JSON types map to language types: `true`/`false` → `bool`, number → `int`/`long`/`decimal`
  (same inference rules as literals), string → `string`, `null` → `null`. Arrays and objects in
  `variables` are rejected in v1 (`INVALID_PARAMETER`), because the language has no parameter syntax for
  them yet.
* Request body limit (default 64 KB, configurable), `Content-Type` validation (`415` for a non-JSON body),
  and malformed JSON → `400 INVALID_REQUEST`.
* Unknown top-level properties are ignored for forward compatibility, and documented as such.
* Body-level errors use the M5.2 envelope with `position` omitted.

### Tests

* POST parity with GET for the same query and variables.
* A long query (still under `MaxInputLength`) succeeds through POST.
* Malformed JSON → `400 INVALID_REQUEST`; wrong content type → `415`.
* Body over the limit → `413`.
* JSON number inference: `1` is `int`, `2147483648` is `long`, `1.5` is `decimal`.
* Array/object variable → `400 INVALID_PARAMETER`; duplicate/invalid/too many variables are rejected.
* Unknown top-level properties are ignored; a non-string `query` is `INVALID_REQUEST`.

### Definition of Done

* GET and POST produce identical results and identical error envelopes.
* No request body is echoed back in an error.

---

## M5.6 — Execution limits and cancellation

**Goal:** hostile or accidental queries cannot consume unbounded time or memory.

### Core changes

* Extend `QueryLimits`:
  * `MaxExecutionTime` (default 2 s);
  * `MaxResultItems` (default 10 000);
  * `MaxSourceItems` already exists from M5.3 and stays enforced while loading;
  * keep `MaxInputLength`, `MaxTokens`, `MaxDepth`.
* Thread a per-execution `ExecutionGuard` (deadline + `CancellationToken`) through the interpreter.
  Check it:
  * at every function call;
  * inside `$filter`, `$map`, `$select`, `$sort`, `$any`, `$first`, `$distinct` loops, once per element.
* Timeout → `EXECUTION_TIMEOUT`; result over the limit → `RESULT_TOO_LARGE`. Both are new stable codes;
  `SOURCE_TOO_LARGE` already exists from M5.3.
* The result limit applies to the top-level array; scalar results such as `$count()` are not affected.
* Client disconnect (`RequestAborted`) stops the query with `OperationCanceledException`; it is not
  reported as a query error.
* Result limit is an **error**, never silent truncation: a truncated result is a wrong result.

### Adapter changes

* Per-request timeout from options, linked with `RequestAborted`.
* `MaxInputLength` applies to `q` and to the POST `query` field; body size is enforced by Kestrel plus
  the adapter.
* Status mapping from M5.2 is applied to the new codes.

### Tests

* A query that exceeds `MaxExecutionTime` (for example a large `$map` over many rows) → `408`.
* A result over `MaxResultItems` → `422 RESULT_TOO_LARGE`; a result exactly at the limit succeeds;
  a scalar result is unaffected; an empty array is allowed at a zero limit.
* A source over `MaxSourceItems` → `422 SOURCE_TOO_LARGE`.
* Cancelling the request stops execution promptly and does not write a response.
* A cancelled `CancellationToken` at the engine level throws `OperationCanceledException`.
* Existing limit tests (`INPUT_TOO_LONG`, `MAX_DEPTH_EXCEEDED`, `TOO_MANY_TOKENS`) still pass and map to
  their documented status.

### Definition of Done

* Every request has a hard upper bound on time and result size.
* Timeout and cancellation are covered by integration tests, not only unit tests.

---

## M5.7 — Hardening, documentation, and close-out

**Goal:** make M5 usable by someone who has never seen the repository, and declare it complete.

### Work

* `docs/HttpApi.md`: endpoints, parameters, variables, error schema, status table, limits, curl examples.
* `docs/Sources.md`: registration, schema, capabilities, row contract, and the whitelist rule.
* README: quickstart (`dotnet run`, `curl` GET and POST), M5 marked ✅.
* Integration test pass:
  * GET and POST happy paths;
  * every error category;
  * concurrency (parallel requests with different variables and sources);
  * no cross-request state;
  * a source with a null field and a missing optional field.
* AOT smoke test: `dotnet publish hosts/FunQuery.Server -c Release -r win-x64` and run the published
  binary against GET and POST.
* Security review checklist:
  * no arbitrary object or source access;
  * no reflection serializer;
  * error messages contain no server paths or stack traces;
  * source names are not enumerable;
  * body and query size limits active.
* Optional, only if cheap: OpenAPI document generated from the endpoints. It was not included.

### Result

* `dotnet test`: 952 unit + 103 integration = 1055 tests green.
* Native AOT publish (`win-x64`) succeeded. The published binary served GET, source + variables, POST,
  and structured errors.
* Concurrency tests cover parallel requests with different sources and variables; each source records
  exactly the requests that used it.
* The security checklist is documented in [`HttpApi.md`](HttpApi.md#security).

### Definition of Done

* All Milestone 5 DoD items in [Milestones.md](Milestones.md#milestone-5-aspnet-core-integration) are
  satisfied by code, tests, and docs.
* `dotnet test` is green, including the new integration test project.
* The published AOT binary serves GET and POST.

---

## Cross-cutting contracts (as implemented)

These are the choices the implementation made. They are documented here so a later change is a
deliberate decision rather than an accident:

1. **One source per request.** `?source=<name>` binds `@source`; multiple sources in one query are out of
   scope for M5. Alternative: `src.<variable>=<name>` for many sources, at the cost of a larger surface.
2. **GET variable syntax.** `v.<name>=<value>` with JSON-scalar-or-string parsing. Alternative: all
   variables are strings, and numeric/date conversion is explicit in the query.
3. **Status codes.** `400` for input errors, `413` for size, `408` for timeout, `422` for result/source
   limits, `500` for internal. Alternative: everything non-internal is `400`.
4. **Eager source loading.** The adapter loads all rows, then the synchronous interpreter runs. Async
   execution is deferred to the provider work in M6.
5. **Result limit behavior.** Error (`RESULT_TOO_LARGE`), never truncation.
6. **Variable types in v1.** Scalars only (`int`, `long`, `decimal`, `string`, `bool`, `null`); arrays and
   objects are rejected until the language has a parameter syntax for them.

## Test matrix summary

| Area                  | Unit (`FunQuery.Tests`) | Integration (`FunQuery.AspNetCore.Tests`) |
| --------------------- | ----------------------- | ----------------------------------------- |
| Typed parameters      | yes                     | yes                                       |
| Source schema         | yes                     | yes                                       |
| Variables             | yes                     | yes                                       |
| Error envelope        | —                       | yes                                       |
| Limits/timeout        | yes                     | yes                                       |
| GET/POST parity       | —                       | yes                                       |
| Concurrency           | —                       | yes                                       |
| AOT publish           | —                       | manual smoke / CI job                     |

## Exit criteria

M5 is complete when:

* `GET /query` and `POST /query` work from a browser and `curl`;
* sources are explicitly registered, schema-typed, and whitelisted;
* variables can be supplied through the query string and the POST body;
* every failure is a structured JSON error with a stable code and, when known, a position;
* time, input, depth, token, parameter, source, and result limits are enforced;
* `docs/HttpApi.md` and `docs/Sources.md` exist and match the implementation;
* the AOT-published host serves both endpoints.
