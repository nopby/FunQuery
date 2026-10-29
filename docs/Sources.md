# FunQuery Sources

A **source** is a data set that a query may read through `$source(@source)`. Only sources registered
at startup can be reached; there is no way to name an arbitrary application object or table. This is
the security boundary described in [Milestone 5](Milestones.md#milestone-5-aspnet-core-integration).

## Registering a source

A source implements `IQuerySource`:

```csharp
public sealed class OrdersSource : IQuerySource
{
    private static readonly ObjectType OrderSchema = new(
        new Dictionary<string, SemanticType>(StringComparer.Ordinal)
        {
            ["id"] = SemanticTypeOptions.Int,
            ["customer"] = SemanticTypeOptions.String,
            ["total"] = SemanticTypeOptions.Decimal,
            ["orderedOn"] = SemanticTypeOptions.Date,
        });

    public string Name => "orders";
    public ObjectType Schema => OrderSchema;
    public SourceCapabilities Capabilities => SourceCapabilities.All;

    public ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(Rows());
}
```

Register it once, before the app starts serving requests:

```csharp
builder.Services.AddFunQuery();
builder.Services.AddFunQuerySources(sources => sources
    .Add(new OrdersSource())
    .Add(new PeopleSource()));
```

The registry is frozen after `AddFunQuerySources` returns. A duplicate name, an invalid name, or a
later `Add` is a startup error (`InvalidOperationException` / `ArgumentException`), not a request error.
Names are case-sensitive and may contain ASCII letters, digits, `_`, `-`, and `.`, up to 64 characters.

`SourceRegistry.Empty` is used when the host registers nothing; in that case only inline
`$source([...])` works.

## Schema

`Schema` is an `ObjectType`: an ordered set of field names and language types. The analyzer sees it
**before any data is loaded**, so these fail during analysis:

```text
$source(@source).$filter(unknownField eq 1)   -- UNKNOWN_IDENTIFIER
$source(@source).$filter(id eq 'x')           -- TYPE_MISMATCH
```

Field order in `Schema` is the field order of the result rows and of `$select({row: ~})`.

Supported field types are the language types: `int`, `long`, `decimal`, `string`, `bool`, `date`,
`array<T>`, and nested `object`. `any` and `unknown` accept any value and are intended for provider
metadata, not for hand-written sources.

## Rows

`LoadAsync` returns one dictionary per row, keyed by field name. The adapter converts each row to the
runtime representation and validates it against `Schema`:

| Situation | Behavior |
| --------- | -------- |
| Field is present with the declared type | Used as-is |
| Field is missing or `null` | Reads as `null`, for any declared type |
| Key is not in the schema | Ignored; it is never exposed to the query |
| Value has the wrong type | `TYPE_MISMATCH`, with the row index and field path (`row 2 field 'address.city'`, `row 0 field 'tags[1]'`) |
| Nested object | A dictionary, converted recursively against the nested `ObjectType` |
| Array | An `IEnumerable<object?>`; each element is converted and validated |

Rows are loaded **after** the query passes analysis. A query with an unknown field therefore never
touches the source, which keeps invalid requests cheap and prevents unnecessary data access.

`MaxSourceItems` (default 100 000) bounds a single load. More rows is `SOURCE_TOO_LARGE` (`422`), never
a silent truncation.

## Binding to the query

The selected source is always bound to the variable `@source`:

```http
GET /query?source=orders&q=$source(@source).$filter(total gt 100).$select(id, customer)
```

The query may use the schema fields directly inside per-element functions, exactly as it uses fields
from an inline array. An inline source still works when a registry is configured:

```text
$source([1, 2, 3]).$count()
```

`@source` is a normal environment parameter, so `$let(@source, ...)` is `VARIABLE_REDEFINED`. There is
no shadowing.

## Capabilities

`Capabilities` declares what a provider can push down:

```text
Filtering, Sorting, Projection, Pagination, StringOperations, DateOperations, Aggregation
```

The in-memory adapter supports everything and ignores the flags. They are stored for the provider work
in Milestone 6, where a provider must reject an operation it cannot translate instead of silently
evaluating it another way.

## Limits and errors

| Error | When |
| ----- | ---- |
| `UNKNOWN_SOURCE` | `source` does not name a registered source (`400`) |
| `SOURCE_TOO_LARGE` | `LoadAsync` returned more than `MaxSourceItems` rows (`422`) |
| `TYPE_MISMATCH` | A row value does not match the declared schema (`400`) |
| `UNKNOWN_IDENTIFIER` / `TYPE_MISMATCH` | The query does not match the schema; reported before loading (`400`) |

See [`HttpApi.md`](HttpApi.md) for the envelope and status table.

## Not yet available

* More than one source per request. The selected source is always `@source`.
* Async/lazy execution; `LoadAsync` is awaited once and the rows are materialized before execution.
* Provider pushdown; the in-memory adapter always materializes every row.
