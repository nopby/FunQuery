# FunQuery HTTP API

This document specifies the HTTP surface of the ASP.NET Core adapter. It currently covers Milestone
5.1–5.2 (GET and the error contract); sources, variables, POST, and execution limits are added in later
stages of [`Milestone5.md`](Milestone5.md).

## Endpoint

```http
GET /query?q=<url-encoded query>
```

| Part | Rule |
| ---- | ---- |
| `q` | Required. The URL-decoded value is the FunQuery expression. An empty or missing value is `INVALID_REQUEST` |
| Result | `200` with the query result as JSON, exactly as `QueryResult.ToJson()` produces it. The result is never wrapped in an envelope |
| Content type | `application/json; charset=utf-8` on success and on every error |
| Route and parameter | Configurable at startup through `FunQueryOptions.Route` and `FunQueryOptions.QueryParameter` |

```bash
curl "http://localhost:5000/query?q=%24source(%5B1%2C2%2C3%5D).%24count()"
# 3
```

The result keeps the language's own JSON encoding: objects are written in field order, arrays in element
order, dates as `"yyyy-MM-dd"`, and `null` as `null`.

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
| `408` | `EXECUTION_TIMEOUT` | The query exceeded its time budget (M5.6) |
| `422` | `RESULT_TOO_LARGE`, `SOURCE_TOO_LARGE` | The query is valid but its result or source is too large (M5.3/M5.6) |
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

Set them at startup:

```csharp
builder.Services.AddFunQuery(options =>
{
    options.Limits = new QueryLimits
    {
        MaxInputLength = 4096,
        MaxTokens = 2048,
        MaxDepth = 32,
    };
});
```

Time, result-size, and source-size limits arrive in M5.6.

## Not yet available

* Registered sources and schema validation (`source` parameter) — M5.3.
* Query variables from the query string (`v.<name>=<value>`) — M5.4.
* `POST /query` — M5.5.
* Execution timeout and result-size limits — M5.6.
* OpenAPI document — M5.7, optional.

## Host setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFunQuery();

var app = builder.Build();

app.MapFunQueryEndpoints();

app.Run();
```

`MapFunQueryEndpoints` adds the error filter to the endpoint, so no separate error middleware is required
for this endpoint.
