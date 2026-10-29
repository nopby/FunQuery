# FunQuery HTTP API

This document specifies the HTTP surface of the ASP.NET Core adapter. Milestone 5 is complete: GET,
POST, the error contract, registered sources, query variables, and execution limits are implemented and
covered by integration tests. See [`Milestone5.md`](Milestone5.md) for the staged plan.

## GET /query

```http
GET /query?q=<url-encoded query>&source=<registered source>
```

| Part | Rule |
| ---- | ---- |
| `q` | Required. The URL-decoded value is the FunQuery expression. An empty or missing value is `INVALID_REQUEST` |
| `source` | Optional. Name of a source registered with `AddFunQuerySources`. The selected source is bound to `@source`; an unknown name is `UNKNOWN_SOURCE` |
| Result | `200` with the query result as JSON, exactly as `QueryResult.ToJson()` produces it. The result is never wrapped in an envelope |
| Content type | `application/json; charset=utf-8` on success and on every error |
| Route and parameters | Configurable at startup through `FunQueryOptions.Route`, `QueryParameter`, and `SourceParameter` |

```bash
curl "http://localhost:5000/query?q=%24source(%5B1%2C2%2C3%5D).%24count()"
# 3
```

The result keeps the language's own JSON encoding: objects are written in field order, arrays in element
order, dates as `"yyyy-MM-dd"`, and `null` as `null`.

## POST /query

```http
POST /query
Content-Type: application/json

{
  "query": "$source(@source).$filter(id gt @min).$take(10)",
  "source": "orders",
  "variables": { "min": 100, "region": "EU" }
}
```

| Field | Rule |
| ----- | ---- |
| `query` | Required string. Same language and same `QueryLimits.MaxInputLength` as GET |
| `source` | Optional string; same registry and `@source` binding as GET |
| `variables` | Optional object of JSON scalars; `null` means no variables. Arrays and objects are rejected (`INVALID_PARAMETER`) |
| Unknown properties | Ignored, so adding a field later does not break older clients |
| Content type | Must be JSON (`application/json` or a `+json` type). Anything else is `UNSUPPORTED_MEDIA_TYPE` (`415`) |
| Body size | At most `FunQueryOptions.MaxRequestBodyBytes` (default 64 KB), otherwise `REQUEST_TOO_LARGE` (`413`) |
| Malformed JSON | `INVALID_REQUEST` (`400`). The body and parser details are never echoed back |

GET and POST share one pipeline, so the same query, source, and variables produce the same result JSON
and the same error envelope. POST exists for payloads that are awkward or too long for a URL.

```bash
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"query":"$source(@source).$filter(id gt @min).$count()","source":"orders","variables":{"min":1}}'
# 4
```

## Sources

`source` selects one source registered at startup and binds it to the variable `@source`:

```bash
curl "http://localhost:5000/query?source=orders&q=%24source(%40source).%24filter(total%20gt%20100).%24select(id%2C%20customer)"
# [{"id":1,"customer":"Ana"},{"id":5,"customer":"Eko"}]
```

The schema is known before loading, so an unknown field or a wrong comparison is rejected without
reading the source. The selected rows are validated against the schema; a mismatch is `TYPE_MISMATCH`.
See [`Sources.md`](Sources.md) for registration, the row contract, capabilities, and limits.

## Variables

On GET, every query parameter named `v.<name>` becomes the variable `@name`. On POST, the same names
come from the `variables` object:

```bash
curl "http://localhost:5000/query?source=orders&v.min=100&q=%24source(%40source).%24filter(total%20gt%20%40min)"
```

| Rule | Detail |
| ---- | ------ |
| Name | ASCII letter or `_` first, then ASCII letters, digits, and `_`. Case-sensitive. `source` is reserved for the selected source |
| JSON scalar | If the URL-decoded value parses as a JSON scalar, its type is used: `"Ana"` → `string`, `123` → number, `true`/`false` → `bool`, `null` → `null` |
| Bare text | Anything that is not a JSON scalar is a `string`: `Ana`, `hello world`, `00123`, and an empty value `v.x=` |
| Numbers | The literal rules apply: `int` if it fits 32 bits, `long` if it fits 64 bits, `decimal` otherwise. A fraction or exponent becomes `decimal`. Outside the decimal range is `INVALID_PARAMETER` |
| Dates | There is no JSON date type; pass the text and convert it in the query: `$date(@raw)` |
| Arrays and objects | Rejected in v1 (`INVALID_PARAMETER`) |

Variables are read-only for the request. `$let(@name, ...)` on an externally supplied name is
`VARIABLE_REDEFINED`; there is no shadowing. Each request has its own environment, so concurrent
requests never see each other's values.

```text
?q=$source(@source).$filter(id gt @min).$select(id, customer)&source=orders&v.min=1
?q=$source([1]).$map(@label)&v.label="Ana"
?q=$source([1]).$map(@active)&v.active=true
?q=$source([{d: '2024-06-01'}]).$filter($date(d) gt $date(@since)).$count()&v.since=2024-03-01
```

Errors:

| Situation | Code |
| --------- | ---- |
| Name is empty, not an identifier, or is `source` | `INVALID_PARAMETER` (`400`) |
| Same `v.<name>` supplied twice | `INVALID_PARAMETER` (`400`) |
| More than `MaxParameters` variables | `INVALID_PARAMETER` (`400`) |
| A value longer than `MaxParameterValueLength` | `INVALID_PARAMETER` (`400`) |
| JSON number outside the supported range | `INVALID_PARAMETER` (`400`) |
| JSON array or object | `INVALID_PARAMETER` (`400`) |
| `@name` is not supplied | `UNDEFINED_VARIABLE` (`400`) |
| `$let` redefines a supplied variable | `VARIABLE_REDEFINED` (`400`) |

## Error envelope

Every failure uses the same JSON document:

```json
{
  "code": "TYPE_MISMATCH",
  "message": "Cannot compare string with int.",
  "position": 17,
  "length": 3
}
```

| Field | Meaning |
| ----- | ------- |
| `code` | Stable error code from `QueryErrorCode`. Never changes when the C# enum is renamed |
| `message` | Human-readable message. It never contains a stack trace, file path, or SQL |
| `position` | 0-based character index in the decoded query. Omitted when the error has no position |
| `length` | Length of the offending part, for highlighting. Omitted when unknown |

Internal failures return `500` with only:

```json
{
  "code": "INTERNAL_ERROR",
  "message": "The query could not be executed."
}
```

The real exception is written to the server log. Unexpected exceptions are handled the same way, so an
unhandled error can never leak an exception type or a stack trace.

## Status mapping

| Status | Codes | Meaning |
| ------ | ----- | ------- |
| `400` | Lexer, parser, analyzer, and runtime input errors: `UNEXPECTED_TOKEN`, `UNKNOWN_FUNCTION`, `UNKNOWN_IDENTIFIER`, `TYPE_MISMATCH`, `INVALID_ARGUMENT_COUNT`, `INVALID_TARGET`, `DUPLICATE_FIELD`, `NUMBER_OUT_OF_RANGE`, `UNDEFINED_VARIABLE`, `INVALID_DATE`, … | The query is invalid |
| `400` | `MAX_DEPTH_EXCEEDED`, `TOO_MANY_TOKENS` | The query is too complex |
| `400` | `INVALID_REQUEST`, `UNKNOWN_SOURCE`, `INVALID_PARAMETER` | The request is malformed |
| `413` | `INPUT_TOO_LONG`, `REQUEST_TOO_LARGE` | The query string or request body is too large |
| `415` | `UNSUPPORTED_MEDIA_TYPE` | A POST body was sent with a non-JSON content type |
| `408` | `EXECUTION_TIMEOUT` | The query exceeded its time budget |
| `422` | `RESULT_TOO_LARGE`, `SOURCE_TOO_LARGE` | The query is valid but its result or source is too large |
| `500` | `INTERNAL_ERROR` | A bug in the server, not in the request |

Codes marked with a stage are part of the contract already; the stage that raises them is noted so the
table and the implementation cannot drift.

The mapping lives in one place, `FunQueryStatusCodes.For(QueryErrorCode)`. A host that needs a different
policy can replace the endpoint filter, but the default is part of the API contract.

## Limits

| Limit | Default | Error |
| ----- | ------- | ----- |
| `MaxInputLength` | 2048 characters | `INPUT_TOO_LONG` (`413`) |
| `MaxTokens` | 1024 | `TOO_MANY_TOKENS` (`400`) |
| `MaxDepth` | 64 (absolute maximum 256) | `MAX_DEPTH_EXCEEDED` (`400`) |
| `MaxSourceItems` | 100 000 rows | `SOURCE_TOO_LARGE` (`422`) |
| `MaxParameters` | 32 | `INVALID_PARAMETER` (`400`) |
| `MaxParameterValueLength` | 1024 characters | `INVALID_PARAMETER` (`400`) |
| `MaxRequestBodyBytes` | 64 KB (option, not a query limit) | `REQUEST_TOO_LARGE` (`413`) |
| `MaxExecutionTime` | 2 seconds | `EXECUTION_TIMEOUT` (`408`) |
| `MaxResultItems` | 10 000 items | `RESULT_TOO_LARGE` (`422`) |

`MaxExecutionTime` is **cooperative**: it is checked between elements and at every function call, not
by aborting a thread. A single long comparison or sort may therefore overshoot the deadline slightly,
but a query that keeps working is stopped at the next check. A client that disconnects sets
`RequestAborted`; execution stops and no response is written.

`MaxResultItems` applies to the **top-level array** of the result. Scalar results such as `$count()` or
`$first()` are not affected, and an empty array is always allowed. Exceeding the limit is an error, never
a silent truncation.

Set them at startup:

```csharp
builder.Services.AddFunQuery(options =>
{
    options.Limits = new QueryLimits
    {
        MaxInputLength = 4096,
        MaxTokens = 2048,
        MaxDepth = 32,
        MaxParameters = 16,
        MaxParameterValueLength = 512,
        MaxExecutionTime = TimeSpan.FromSeconds(1),
        MaxResultItems = 5000,
    };
});
```

## Security

The adapter is meant to accept input from untrusted clients. The rules below are part of the contract:

| Area | Rule |
| ---- | ---- |
| Data access | Only sources registered at startup are reachable. `SourceRegistry` has no enumeration API, and an unknown name returns `UNKNOWN_SOURCE` without listing registered sources |
| Serialization | Request and error DTOs use a source-generated `JsonSerializerContext`; query results use the manual `ValueJson` writer. No reflection-based serializer |
| Errors | Non-internal errors return the stable code and message only. Internal errors return `INTERNAL_ERROR` with a generic message; the real exception goes to the server log. No stack traces, file paths, SQL, or request bodies are returned |
| Query limits | Input length, token count, parser depth, source rows, parameter count/value length, body bytes, execution time, and result items are enforced before or during execution |
| Cancellation | Client disconnect sets `RequestAborted`; execution stops and no response is written |
| AOT | The sample host is published with Native AOT; endpoints use `RequestDelegate`, so there is no reflection-based handler binding |

See [`Sources.md`](Sources.md) for the source whitelist and row contract.

## Not included

* OpenAPI document. It is optional and was not part of the M5 definition of done.

## Host setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFunQuery();
builder.Services.AddFunQuerySources(sources => sources.Add(new OrdersSource()));

var app = builder.Build();

app.MapFunQueryEndpoints();

app.Run();
```

`MapFunQueryEndpoints` maps both GET and POST; each handler owns its error path, so no separate error
middleware is required. The endpoints use `RequestDelegate`, which keeps them compatible with Native
AOT.

Publish the sample host as a native binary:

```bash
dotnet publish hosts/FunQuery.Server -c Release -r win-x64
```
