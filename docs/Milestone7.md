# Milestone 7 — Developer Experience: Work Plan

This document expands the new developer-experience milestone into stages. The goal is to make FunQuery
easy to discover, try, and integrate without changing the language, the HTTP contract, or the provider
semantics.

## Principles

* **Developer tools are hosts, not the language.** OpenAPI, the docs UI, and the playground live in the
  adapter/sample hosts; the core stays free of HTTP and UI concerns.
* **Opt-in and development-first.** API docs and the playground are disabled by default in production.
* **Same contract.** The docs and playground describe the real GET/POST behavior; they never introduce a
  second API.
* **AOT-aware.** Prefer source-generated JSON and static assets. If OpenAPI generation conflicts with
  Native AOT, document the limitation and keep the AOT host free of the docs tooling.
* **No secrets.** No connection strings, SQL, or internal paths in the generated document or UI.

## Stage overview

| Stage | Name                                      | Depends on | Size | Status |
| ----- | ----------------------------------------- | ---------- | ---- | ------ |
| M7.1  | OpenAPI document                           | M6         | M    | ⏳     |
| M7.2  | Interactive API docs UI                    | M7.1       | S    | ⏳     |
| M7.3  | Playground polish and integration          | M7.1       | S    | ⏳     |
| M7.4  | Local development environment              | M6         | M    | ⏳     |
| M7.5  | Documentation and close-out                | M7.1–M7.4  | S    | ⏳     |

---

## M7.1 — OpenAPI document

**Goal:** publish a machine-readable OpenAPI document for the GET/POST endpoints.

### Work

* Add `Microsoft.AspNetCore.OpenApi` to the adapter or sample host.
* Map `GET /openapi/v1.json` (or the configured path) with `AddOpenApi()` / `MapOpenApi()`.
* Describe:
  * `GET /query`: `q`, `source`, and the dynamic `v.<name>` variables (documented as a convention,
    because OpenAPI has no wildcard query parameter);
  * `POST /query`: the `QueryRequest` body (`query`, `source`, `variables`);
  * success responses: arbitrary JSON result, with examples;
  * errors: the structured `{code, message, position, length}` envelope and the status table from
    [`HttpApi.md`](HttpApi.md);
  * tags, summaries, descriptions, and examples.
* Keep the result schema free-form (`{}` / JSON value) with a clear description; the language result
  shape depends on the query.
* Gate generation behind an option (for example `EnableApiDocs`) so production hosts can omit it.
* AOT assessment: if the built-in generator produces trimming/AOT warnings, fall back to a static
  hand-written document or keep OpenAPI in the non-AOT host.

### Tests

* The document is valid JSON and contains both paths and both methods.
* `QueryRequest`, `QueryErrorResponse`, and the error statuses are present.
* Examples do not contain credentials, SQL, or server paths.
* The endpoint is absent when the option is disabled.
* AOT publish remains clean, or the limitation is documented and tested.

### Definition of Done

* A client can generate a request from the document.
* The document matches the real endpoints and error envelope.
* The AOT strategy is explicit.

---

## M7.2 — Interactive API docs UI

**Goal:** a browsable docs page, comparable to Scalar or Swagger UI.

### Work

* Add Scalar (`Scalar.AspNetCore`) or Swagger UI as a static-asset UI.
* Map it at `/docs` (or `/scalar/v1`) only when `EnableApiDocs` is true.
* Point it at the OpenAPI document from M7.1.
* Apply the same security posture as the playground:
  * CSP `default-src 'self'`, no external CDN;
  * `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`;
  * no inline secrets.
* Link the UI from the README and the playground.

### Tests

* The UI returns `200` and the expected HTML when enabled.
* The page returns `404` when disabled.
* The OpenAPI URL is referenced correctly.
* Security headers are present.

### Definition of Done

* A developer can open the docs page, inspect both endpoints, and try them.
* Disabling the option removes both the document and the UI.

---

## M7.3 — Playground polish and integration

**Goal:** make the existing playground the fastest way to go from an idea to a working query.

### Work

* Add a link from the playground to the API docs.
* Add “Copy as curl” and “Copy as POST body” actions for the current query, source, and variables.
* Show the effective request (method, URL/body) next to the result.
* Improve examples with a PostgreSQL source example and a variables example.
* Keep the existing error position highlighting and history.
* Preserve the opt-in `Playground:Enabled` / `ExposeSourceNames` behavior.

### Tests

* The page contains the docs link and the copy actions.
* CSP remains valid; no external resources.
* The page still returns `404` when disabled.
* A manual smoke test covers copy actions and a query run.

### Definition of Done

* A developer can copy a working `curl` command from the playground.
* The playground links to the generated API docs.

---

## M7.4 — Local development environment

**Goal:** start FunQuery and PostgreSQL with one documented command, without hand-written setup.

### Work

* `deploy/compose.yaml` for PostgreSQL (Podman/Docker compatible), with a named volume and healthcheck.
* `scripts/dev-postgres.ps1` (Windows) and `scripts/dev-postgres.sh` (Unix) to start/stop/reset the
  container.
* `db/seed.sql` creating the sample `orders` table and rows used by `hosts/FunQuery.PostgreSql.Server`.
* `.env.example` with `FUNQUERY_POSTGRES`.
* `docs/Development.md`: start the database, seed it, run the in-memory host, run the PostgreSQL host,
  run the tests, and clean up.
* Keep the in-memory host runnable without a database.

### Tests

* The seed script is idempotent (`DROP IF EXISTS` / `CREATE`).
* The documented connection string works with the sample host.
* A fresh clone can follow the guide and run both hosts.
* CI remains able to run the in-memory tests without PostgreSQL.

### Definition of Done

* A new contributor can run the playground and the PostgreSQL host from the guide.
* No database setup step is hidden or implicit.

---

## M7.5 — Documentation and close-out

**Goal:** document the developer experience and declare M7 complete.

### Work

* `docs/OpenApi.md` (or `docs/DeveloperExperience.md`): endpoints, document URL, UI URL, options,
  examples, AOT notes.
* Update `README.md` with the docs/playground links and mark M7 ✅.
* Update `docs/Milestones.md` and the roadmap.
* Final `dotnet test`, PostgreSQL integration run, and AOT smoke.
* Record the supported/unsupported docs features.

### Definition of Done

* All M7 stages are ✅.
* The OpenAPI document and docs UI match the implementation.
* The local development guide is tested on a fresh environment.
* The security checklist from M5 still holds.

---

## Cross-cutting decisions

1. **OpenAPI generator vs static document.** Prefer the built-in generator; fall back to a static
   document if AOT/trimming makes it impractical.
2. **UI choice.** Scalar is the default recommendation; Swagger UI is an acceptable alternative.
3. **Gating.** `EnableApiDocs` (and the existing `Playground:Enabled`) default to false; the sample hosts
   may enable them in Development.
4. **Dynamic variables.** `v.<name>` cannot be expressed as a normal OpenAPI parameter; document it as a
   convention with examples.
5. **Authentication.** None in v1; a host can put the endpoints behind its own auth middleware.

## Exit criteria

* `GET /openapi/v1.json` describes GET/POST, request bodies, results, and the error envelope.
* A docs UI is available in development and absent when disabled.
* The playground links to the docs and can copy a working `curl` command.
* PostgreSQL and the in-memory host start from the local development guide.
* M7 is marked complete with tests and documentation.
