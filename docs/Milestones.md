# FunQuery Milestones

This document defines the development roadmap for FunQuery.

The milestones are intentionally incremental. Each milestone should produce a coherent improvement to the language rather than merely adding isolated implementation details.

## Milestone 0 — Fondasi

**Goal:** establish a stable foundation for the language implementation and its documentation.

### Scope

* xUnit test project is available.
* Operator documentation is established.
* Data type documentation is established.
* Equality semantics for `float` and `double` are explicitly decided.
* The solution builds without warnings.

### Operator Documentation

The language specification must document:

* operator name;
* operand types;
* result type;
* null behavior;
* numeric behavior;
* provider expectations where relevant.

### Numeric Equality

Equality semantics for `float` and `double` must be explicitly documented before the language is considered stable.

This decision must apply consistently to:

* in-memory execution;
* LINQ execution;
* SQL translation where supported.

### Definition of Done

* Solution builds without warnings.
* xUnit test project runs successfully.
* Operator documentation is finalized.
* Data type documentation is finalized.
* `float`/`double` equality semantics are documented and tested.

---

# Milestone 1 — Hardening Front-end

**Goal:** make parsing predictable and safe when processing untrusted input.

### Scope

Introduce a structured:

```text
QueryException
```

with:

* error code;
* message;
* source position.

The front-end must also enforce:

* maximum input length;
* maximum parser recursion depth.

The grammar must enforce:

* the top-level expression is a function call;
* the head of a chain is a function call;
* `Named` arguments are allowed only inside `{ }`;
* trailing commas are rejected;
* identifiers are case-sensitive;
* `$order` is replaced by `$sort`;
* `$groupby` is removed.

### Examples

Invalid deeply nested input:

```text
((((((((((((((((...
```

must produce a controlled query error instead of causing a stack overflow or process crash.

Invalid trailing comma:

```text
$source([1, 2,])
```

must be rejected.

Function names and identifiers remain case-sensitive.

### Definition of Done

* Every parser error has a stable error code.
* Every parser error has a source position.
* Excessively long input is rejected.
* Excessive recursion is rejected.
* Invalid top-level expressions are rejected.
* Invalid chain heads are rejected.
* `Named` arguments outside objects are rejected.
* Trailing commas are rejected.
* `$sort` is the supported ordering function name.
* `$order` is not supported.
* `$groupby` is not supported.
* Adversarial parser input produces an error rather than a crash.

---

# Milestone 2 — First Vertical Slice

**Goal:** execute a complete FunQuery query against an in-memory data source.

This is the first end-to-end executable milestone.

## Supported Functions

```text
$source(...)
$filter(...)
```

## Supported Values

```text
true
false
null
```

along with the value types already supported by the language.

A `NullType` is introduced as a first-class semantic type.

## Supported Filter Operators

The first filter implementation supports:

```text
eq
neq
gt
gte
lt
lte
```

## Example

```text
$source([
    {id: 1, name: 'hello'},
    {id: 2, name: 'world'}
]).$filter(id eq 2)
```

Expected result:

```text
[
    {id: 2, name: 'world'}
]
```

Another example:

```text
$source([
    {id: 1, name: 'hello'},
    {id: 2, name: 'world'}
]).$filter(id gte 1)
```

Expected result:

```text
[
    {id: 1, name: 'hello'},
    {id: 2, name: 'world'}
]
```

## Runtime Model

The milestone introduces an in-memory execution layer.

Conceptually:

```text
Query
  │
  ▼
Lexer
  │
  ▼
Parser
  │
  ▼
Semantic Analyzer
  │
  ▼
Program
  │
  ▼
In-Memory Interpreter
  │
  ├── $source
  └── $filter
```

`$source` creates an in-memory sequence.

`$filter` evaluates its predicate against each element of that sequence.

Within a filter predicate:

```text
id eq 2
```

the identifier `id` resolves against the current element.

## Definition of Done

* `NullType` exists.
* `true`, `false`, and `null` can be parsed and analyzed.
* `$source` executes in memory.
* `$filter` executes in memory.
* Field identifiers resolve against the current element.
* Comparison operators execute correctly.
* An empty filter result is represented as an empty sequence.
* End-to-end tests cover successful queries.
* End-to-end tests cover empty results.
* The following query works:

```text
$source([
    {id: 1, name: 'hello'},
    {id: 2, name: 'world'}
]).$filter(id eq 2)
```

and returns:

```text
[
    {id: 2, name: 'world'}
]
```

---

# Milestone 3 — Completing the Language

**Goal:** introduce variables, richer expressions, and complete the basic value model.

### Scope

Introduce:

```text
@
~
$let
$field
```

Support:

```text
$source(@variable)
```

Replace the existing global identifier mechanism with an explicit environment model.

Add field paths:

```text
a.b
```

Support escaped single quotes:

```text
'it''s'
```

Support negative numbers:

```text
-10
-1.5
```

Add operators/functions:

```text
not
in
contains
startswith
endswith
```

Support heterogeneous arrays and objects.

### Example

```text
$let(
    @items,
    [
        {id: 1, name: 'hello'},
        {id: 2, name: 'world'}
    ]
)
```

The exact `$let` syntax is subject to the language grammar specification.

### Environment

The execution model moves from a collection of globally known identifiers toward an environment:

```text
Environment
├── variables
├── current element
├── parent environment
└── query parameters
```

This environment becomes the foundation for later provider implementations.

### Definition of Done

Every syntax element documented in the prefix-language specification can be:

1. lexed;
2. parsed;
3. semantically analyzed;
4. executed in memory.

---

# Milestone 4 — Function Set v1

**Goal:** provide the first practical collection of query operations.

### Collection Functions

```text
$select
$map
$sort
$take
$skip
$first
$count
$any
$distinct
$index
```

### Scalar Functions

```text
$lower
$upper
$length
$coalesce
$date
```

### Function Contracts

Every function must have a documented contract containing:

* function name;
* arguments;
* argument types;
* return type;
* null behavior;
* collection behavior;
* error behavior;
* provider requirements where applicable.

### Definition of Done

* Every v1 function is implemented.
* Every function has a written contract.
* Every function has conformance tests.
* In-memory behavior matches its documented contract.

---

# Milestone 5 — ASP.NET Core Integration

**Goal:** expose FunQuery safely through HTTP.

## GET

Provide an endpoint such as:

```http
GET /query?q=...
```

The query language remains independent of ASP.NET Core.

The HTTP adapter is responsible for:

* obtaining the query string;
* obtaining variables from query parameters;
* resolving a registered source;
* executing the query;
* serializing the result;
* returning structured errors.

## POST

A POST interface is also provided for clients that need to send larger query payloads without putting the entire query into the URL.

## Source Registry

External sources must be explicitly registered.

A source registration includes a whitelist and schema information such as:

```text
source
├── name
├── available fields
├── field types
└── capabilities
```

A query cannot access an arbitrary application object or arbitrary data source.

## Error Format

HTTP errors use a structured JSON format containing information such as:

```json
{
  "code": "QUERY_PARSE_ERROR",
  "message": "...",
  "position": 17
}
```

The exact response schema will be specified separately.

## Limits

The HTTP integration must enforce limits for:

* query execution time;
* maximum result/item count;
* input size;
* parser depth;
* available functions;
* available sources.

### Definition of Done

* FunQuery can be called from a browser.
* FunQuery can be called with `curl`.
* GET endpoint is available.
* POST endpoint is available.
* Sources are explicitly whitelisted.
* Source fields and types are defined.
* Query variables can be supplied through query parameters.
* Errors are returned as structured JSON.
* Security and resource limits are active.

---

# Milestone 6 — Providers

**Goal:** execute the same semantic query against external data providers.

## Provider Abstraction

Introduce a provider interface and a capability model.

Conceptually:

```text
FunQuery
   │
   ▼
Semantic Query
   │
   ├── In-Memory Provider
   ├── LINQ Provider
   └── SQL Provider
```

Providers declare the operations they support.

For example:

```text
Capability
├── filtering
├── sorting
├── projection
├── pagination
├── string operations
├── date operations
└── aggregation
```

## LINQ

The first external provider is LINQ:

```text
FunQuery
   ↓
Expression<Func<T, bool>>
   ↓
IQueryable<T>
```

The provider must preserve the semantics of the language rather than merely performing textual translation.

## SQL

The SQL provider translates supported expressions into parameterized SQL.

Values must be passed as typed parameters rather than concatenated into SQL text.

The provider should use source schema information to determine:

* available columns;
* column types;
* supported operations;
* capabilities.

## Definition of Done

A supported query produces equivalent results when executed:

```text
FunQuery
   ├── In-Memory
   ├── LINQ
   └── SQL
```

Provider-specific limitations must be explicit through the capability model.

---

# Milestone Dependencies

The milestones intentionally form a progression:

```text
M0 Fondasi
     │
     ▼
M1 Front-end Hardening
     │
     ▼
M2 In-Memory Vertical Slice
     │
     ▼
M3 Complete Language
     │
     ▼
M4 Function Set v1
     │
     ▼
M5 ASP.NET Core
     │
     ▼
M6 Providers
```

The project should not depend on SQL or an HTTP endpoint to validate the language.

The in-memory interpreter is the reference execution model for the early language.

## Reference Principle

When there is ambiguity about language behavior, the implementation should first define the behavior at the language/semantic level.

Providers then implement that behavior where their capabilities allow it.

This prevents the language from becoming accidentally defined by a particular database or ORM.

## Status Convention

Milestones use the following status indicators:

* 🚧 In progress
* ⏳ Planned
* ✅ Complete

A milestone is considered complete only when its Definition of Done is satisfied by implementation, tests, and documentation.
