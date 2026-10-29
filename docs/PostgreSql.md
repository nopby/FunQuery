# PostgreSQL Provider

This document describes the PostgreSQL provider added in Milestone 6. It is the first implemented SQL
provider; the architecture is in [`Providers.md`](Providers.md) and the staged plan is in
[`Milestone6.md`](Milestone6.md).

## Status

* PostgreSQL dialect, SQL rendering, Npgsql execution, HTTP integration, parity tests, and Native AOT
  publish are implemented.
* The in-memory provider remains the semantic reference.
* The provider is **read-only**: it issues generated `SELECT` statements only. There are no writes,
  DDL, transactions, or multiple statements.

## Registration

```csharp
using FunQuery.AspNetCore;
using FunQuery.PostgreSql;
using FunQuery.SemanticTypes;
using Npgsql;

var dataSource = NpgsqlDataSource.Create(connectionString);

var ordersSchema = new ObjectType(
    new Dictionary<string, SemanticType>(StringComparer.Ordinal)
    {
        ["id"] = SemanticTypeOptions.Int,
        ["customer"] = SemanticTypeOptions.String,
        ["total"] = SemanticTypeOptions.Decimal,
        ["ordered_on"] = SemanticTypeOptions.Date,
    });

var orders = new PostgreSqlSource(
    name: "orders",
    schema: ordersSchema,
    table: "orders",
    dataSource: dataSource,
    commandTimeout: TimeSpan.FromSeconds(10));

builder.Services.AddFunQuery();
builder.Services.AddFunQuerySources(sources => sources.Add(
    orders.AsQuerySource("orders", ordersSchema)));
```

The selected source is always bound to `@source`:

```text
$source(@source).$filter(total gt 100).$select(id, customer)
```

`AddFunQueryPostgreSql(options => ...)` registers `PostgreSqlOptions`, the dialect, and a shared
`NpgsqlDataSource` when a DI-owned data source is preferred. Sources still need to be registered in
`SourceRegistry` before the host is built.

The sample host is `hosts/FunQuery.PostgreSql.Server`:

```bash
FUNQUERY_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres" \
  dotnet run --project hosts/FunQuery.PostgreSql.Server
```

It registers a PostgreSQL `orders` source and an in-memory `people` source.

## Schema mapping

| FunQuery | PostgreSQL | Notes |
| -------- | ---------- | ----- |
| `int` | `integer` | |
| `long` | `bigint` | |
| `decimal` | `numeric` | exact; no binary floating point |
| `string` | `text` | comparisons use `COLLATE "C"` |
| `bool` | `boolean` | |
| `date` | `date` | no time or time zone |
| `array<T>` | `jsonb` or `T[]` | not supported in predicates or nested projections in v1 |
| `object` | `jsonb` | scalar object projections are supported; nested objects are not |

A nullable column maps to a nullable FunQuery field. A missing result column reads as `null`.

## Query examples

```bash
# filter + projection
curl "http://localhost:5000/query?source=orders&q=%24source(%40source).%24filter(total%20gt%20100).%24select(id%2C%20customer)"

# variables
curl "http://localhost:5000/query?source=orders&v.min=100&q=%24source(%40source).%24filter(total%20gt%20%40min).%24count()"

# POST
curl -X POST http://localhost:5000/query \
  -H "Content-Type: application/json" \
  -d '{"query":"$source(@source).$filter(id gt @min).$count()","source":"orders","variables":{"min":1}}'
```

## Supported subset

| Feature | Status |
| ------- | ------ |
| `$source(@source)` (registered source) | supported |
| `$source([...])` (inline source) | `NOT_SUPPORTED` |
| `$filter` | supported |
| `$map` scalar / top-level object literal | supported |
| `$select` list and object form with scalar fields | supported |
| Nested object/array projection | `NOT_SUPPORTED` (JSON projection) |
| `$sort` | supported |
| `$take` / `$skip` after `$sort` | supported |
| `$take` / `$skip` without `$sort` | `ORDER_REQUIRED` |
| `$count`, `$any`, `$first` | supported |
| `$distinct()` | supported |
| `$distinct(key)` | supported via `DISTINCT ON`; order unspecified |
| `$index()` / `$index(base)` after `$sort` | supported via `ROW_NUMBER()` |
| `$index` without `$sort` | `ORDER_REQUIRED` |
| `$index` in a `$distinct` key | `NOT_SUPPORTED` |
| `$coalesce` | supported |
| `$date('yyyy-MM-dd')` and `$date(textField)` | supported |
| `$lower` / `$upper` on ASCII | supported |
| `$lower` / `$upper` with a non-ASCII literal | `NOT_SUPPORTED` |
| `$length` on ASCII | supported |
| `$length` with a non-BMP literal | `NOT_SUPPORTED` |
| `eq`, `neq` | supported; null-safe `IS [NOT] DISTINCT FROM` |
| `gt`, `gte`, `lt`, `lte` | supported |
| `and`, `or`, `not` | supported; two-valued via `COALESCE(..., FALSE)` |
| `in` with an array literal | supported |
| `in` with an array field | `NOT_SUPPORTED` in v1 |
| `contains`, `startswith`, `endswith` | supported |
| `$field('name')`, `$field(@letLiteral)` | supported |
| `$field(@requestParameter)` | `NOT_SUPPORTED` (dynamic identifier) |
| `$let` and request variables | supported |
| Writes, DDL, transactions | not exposed |

## Null semantics

SQL is three-valued; FunQuery is two-valued. The renderer:

* uses `IS NOT DISTINCT FROM` for `eq` and `IS DISTINCT FROM` for `neq`, so
  `null eq null` is true, `null neq null` is false, and `null neq 5` is true;
* wraps ordering/text/membership predicates in `COALESCE(..., FALSE)` so a null operand is false,
  never unknown;
* uses `NULLS FIRST` for ascending and `NULLS LAST` for descending, matching "null is the smallest".

## Ordering and pagination

`$take`/`$skip` require an explicit `$sort`; without it SQL row order is unspecified and the provider
returns `ORDER_REQUIRED`. `$distinct(key)` uses `DISTINCT ON`; order after `$distinct` is unspecified
unless followed by `$sort`.

`$index` requires `$sort` and is rendered as `ROW_NUMBER() OVER (ORDER BY ...)`. In a predicate it is
computed in a derived table, because PostgreSQL does not allow window functions in `WHERE`.

## Parameters and security

* Every user value is a typed `NpgsqlParameter`; no value is concatenated into SQL.
* Identifiers come from the registered schema and are quoted by the dialect.
* Dynamic `$field(@requestParameter)` is rejected; use a literal or `$let`.
* Connection/command failures become a generic `500 INTERNAL_ERROR`; details go to the server log.
  SQL text and connection strings never appear in client errors.
* The HTTP limits from Milestone 5 still apply. `MaxResultItems` is enforced while reading rows, and
  the command timeout is the smaller of the source timeout and `MaxExecutionTime`.
  `MaxSourceItems` is an in-memory loading limit and does not apply to a database source.

## Testing

Live tests use:

```text
FUNQUERY_POSTGRES=Host=127.0.0.1;Port=5433;Username=postgres;Password=postgres;Database=postgres
```

Without it, live tests return early; golden SQL and unit tests still run.

```bash
dotnet test
```

The PostgreSQL project covers:

* golden SQL (no database);
* parameter binder and row reader unit tests;
* live integration and in-memory/PostgreSQL parity tests;
* HTTP GET/POST, both source kinds, structured errors, concurrency, and limits.

## AOT

The sample PostgreSQL host is published with Native AOT:

```bash
dotnet publish hosts/FunQuery.PostgreSql.Server -c Release -r win-x64
```

The AOT binary was smoke-tested against PostgreSQL 18 for GET, POST, and the in-memory source.
Npgsql 10.0.3 is the driver version used.

## Troubleshooting

| Symptom | Meaning |
| ------- | ------- |
| `ORDER_REQUIRED` | Add `$sort` before `$take`/`$skip`, or before `$index` |
| `NOT_SUPPORTED` | The query uses a feature outside the supported subset |
| `TYPE_MISMATCH` | A result column does not match the declared schema |
| `UNKNOWN_IDENTIFIER` | The query references a field not in the source schema |
| `UNKNOWN_SOURCE` | The source name is not registered |
| `INTERNAL_ERROR` | Npgsql/connection failure; inspect the server log |
