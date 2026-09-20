# FunQuery

FunQuery is a small, extensible query language designed to express data queries as a functional expression chain.

The language is intended to be usable directly as a query string, making it suitable for HTTP `GET` endpoints while keeping the language independent from HTTP and from any particular data provider.

The first execution target is an **in-memory interpreter**. Later milestones will allow the same query representation to be translated to LINQ and SQL providers.

## Example

A FunQuery query can start with an in-memory source:

```text
$source([
    {id: 1, name: 'hello'},
    {id: 2, name: 'world'}
])
```

and continue with a filter:

```text
$source([
    {id: 1, name: 'hello'},
    {id: 2, name: 'world'}
]).$filter(id eq 2)
```

The expected result is:

```text
[
    {id: 2, name: 'world'}
]
```

The same query can eventually be transported as an HTTP query parameter:

```http
GET ?$q=$source([...]).$filter(id%20eq%202)
```

The HTTP layer is intentionally separate from the language itself.

## Design Goals

FunQuery is being developed around several principles:

* **Small language surface** — the language should be understandable and predictable.
* **Functional query model** — queries are expressed as composable function calls.
* **Provider independence** — parsing and semantic analysis should not depend on how data is stored.
* **In-memory first** — the language should have a complete executable model before database providers are introduced.
* **Safe by default** — malformed or excessively complex input must fail with controlled errors.
* **HTTP friendly** — the syntax should be practical as a URL query parameter.
* **Typed semantics** — expressions should be analyzed before execution.
* **Extensible functions** — query operations should be registered rather than hard-coded throughout the parser.
* **Same query, different providers** — a query should eventually produce equivalent results in memory, LINQ, and SQL where the provider supports the required capabilities.

## Architecture

FunQuery separates the language pipeline from execution:

```text
                    FunQuery Query
                         │
                         ▼
                       Lexer
                         │
                         ▼
                       Parser
                         │
                         ▼
                         AST
                         │
                         ▼
                 Semantic Analyzer
                         │
                         ▼
                  Semantic Model
                         │
             ┌───────────┼───────────┐
             ▼           ▼           ▼
          In-Memory     LINQ         SQL
         Interpreter   Provider     Provider
```

The parser and semantic analyzer are not database-specific.

This separation is important because the same expression should eventually be executable against different providers.

## Current Status

FunQuery is being developed incrementally.

The roadmap is divided into vertical milestones. Each milestone has a concrete definition of done and should leave the project in a usable state.

| Milestone | Name                     | Status |
| --------- | ------------------------ | ------ |
| 0         | Fondasi                  | ✅     |
| 1         | Hardening front-end      | ⏳      |
| 2         | First vertical slice     | ⏳      |
| 3         | Completing the language  | ⏳      |
| 4         | Function set v1          | ⏳      |
| 5         | ASP.NET Core integration | ⏳      |
| 6         | Providers                | ⏳      |

See [`docs/milestones.md`](docs/milestones.md) for the detailed roadmap.

## Language Direction

The initial language supports function-based query chains:

```text
$source(...)
    .$filter(...)
```

The language will grow toward expressions such as:

```text
$source(...)
    .$filter(...)
    .$select(...)
    .$sort(...)
    .$take(...)
```

Additional language features will be introduced incrementally rather than all at once.

## Error Handling

Invalid input is part of the language contract.

The parser and semantic analyzer should report structured errors containing:

* an error code;
* a human-readable message;
* the position of the offending input.

Malformed input must produce a controlled `QueryException` rather than an unhandled parser/runtime failure.

## Security

FunQuery is intended to accept input from external clients.

Therefore, limits on:

* input length;
* parser recursion depth;
* execution time;
* result/item count;
* available functions;
* available data sources;

are part of the design rather than optional application-level concerns.

Provider integrations must also avoid turning user input into raw executable SQL.

## Development

The implementation is developed in milestones so that language syntax, semantic behavior, runtime behavior, and provider behavior can evolve independently.

The first executable target is:

```text
$source([...]).$filter(...)
```

running entirely in memory.

Only after that vertical slice is stable will additional functions and external providers be added.