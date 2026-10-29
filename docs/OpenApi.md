# OpenAPI and API Docs

FunQuery exposes a machine-readable OpenAPI 3.1 document and an interactive Scalar API reference. Both
describe the real HTTP contract from [`HttpApi.md`](HttpApi.md); they are developer tools, not a second
API.

## URLs and options

| Endpoint | Default | Option |
| -------- | ------- | ------ |
| OpenAPI document | `GET /openapi/v1.json` | `EnableOpenApi`, `OpenApiRoute` |
| Scalar UI | `GET /docs` | `EnableApiDocs`, `ApiDocsRoute` |
| Playground | `GET /playground/` | host-specific `Playground:Enabled` |

`EnableOpenApi` and `EnableApiDocs` default to `false`. `EnableApiDocs` requires `EnableOpenApi`; the
configuration is rejected at startup when the UI is enabled without the document. The sample hosts enable
both in development.

```csharp
builder.Services.AddFunQuery(options =>
{
    options.EnableOpenApi = true;
    options.EnableApiDocs = true;
    options.OpenApiRoute = "/openapi/v1.json";
    options.ApiDocsRoute = "/docs";
});

var app = builder.Build();

if (app.Services.GetRequiredService<FunQueryOptions>().EnableApiDocs)
    app.UseFunQueryApiDocsSecurityHeaders();

app.MapFunQueryEndpoints();
```

The sample host configuration is:

```json
{
  "OpenApi": { "Enabled": true },
  "ApiDocs": { "Enabled": true }
}
```

The document reflects `Route`, `QueryParameter`, and `SourceParameter`; changing those options changes
the paths and parameter names in the document. `ApiDocsRoute` changes the UI path.

## Document contents

The document contains:

* `GET /query` with `q`, `source`, and the `v.<name>` variable convention;
* `POST /query` with the `QueryRequest` body (`query`, `source`, `variables`);
* `QueryRequest` and `QueryErrorResponse` schemas;
* a free-form 200 result with examples;
* `400`, `408`, `413`, `415`, `422`, and `500` error responses with the structured envelope.

`v.<name>` is documented as a convention because OpenAPI has no wildcard query parameter. The document
does not contain connection strings, SQL, server paths, or credentials.

Examples:

```bash
curl "http://localhost:5000/query?q=%24source(%5B1%2C2%2C3%5D).%24count()"
curl "http://localhost:5000/openapi/v1.json"
```

```bash
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"query":"$source(@source).$filter(total gt @min).$select(id, customer)","source":"orders","variables":{"min":100}}'
```

## Scalar UI

The UI is served by `Scalar.AspNetCore` 2.18.2 and points at the configured OpenAPI URL. Assets are
bundled locally; the page loads no CDN or external fonts.

The sample hosts call `UseFunQueryApiDocsSecurityHeaders`, which adds:

* a per-request nonce CSP (`script-src 'self' 'nonce-...'`, `style-src 'self' 'unsafe-inline'`);
* `X-Content-Type-Options: nosniff`;
* `X-Frame-Options: DENY`;
* `Referrer-Policy: no-referrer`;
* `Cache-Control: no-store`.

The playground footer links to `/docs`; the README links to the playground, docs UI, and document.

## Playground integration

The playground shows the effective `POST /query` URL and body, and can copy the request as a POSIX-shell
`curl` command or as a pretty JSON body. See [`HttpApi.md`](HttpApi.md) for the playground behavior and
[`Development.md`](Development.md) for local startup.

## Native AOT

The OpenAPI document is built directly as `JsonNode` with a non-generic array helper. It does not depend
on `Microsoft.AspNetCore.OpenApi`, endpoint metadata, or reflection-based schema generation, so it stays
AOT-safe. Scalar serves bundled static assets.

Both sample hosts are published with Native AOT:

```bash
dotnet publish hosts/FunQuery.Server -c Release -r win-x64
dotnet publish hosts/FunQuery.PostgreSql.Server -c Release -r win-x64
```

Run the published binaries from their output directory so `wwwroot` and `appsettings.json` are found.
The in-memory AOT host was smoke-tested for the document, UI, playground, and a query; the PostgreSQL AOT
host was smoke-tested for both provider-backed and in-memory sources.

## Supported and unsupported docs features

Supported:

* static OpenAPI 3.1 for GET/POST, schemas, examples, and the error envelope;
* configurable document/UI routes and query/source parameter names;
* Scalar UI with bundled assets, nonce CSP, and the documented security headers;
* Native AOT publishing of both sample hosts.

Not supported:

* authentication/security schemes in the document; put the endpoints behind host middleware;
* multiple documents, API versioning, or remote OpenAPI document sources;
* a formal OpenAPI parameter for dynamic `v.<name>` variables;
* Swagger UI (Scalar is the chosen UI);
* guaranteed client SDK generation; Scalar's SDK controls are outside the FunQuery contract;
* the playground in the PostgreSQL host (it lives in the in-memory sample host).

The document is maintained as a static builder in the adapter. Adding a new HTTP endpoint requires
updating `FunQueryOpenApiDocument`; it is not inferred from endpoint metadata.

## Tests

The adapter tests cover both methods, schemas, status codes, disabled behavior, custom routes and
parameters, the UI, the configured document URL, and security headers/nonces. The full suite also runs the
PostgreSQL integration tests when `FUNQUERY_POSTGRES` is set.

```bash
dotnet test
```
