# FunQuery Functions: `$select` and `$map`

This document specifies the first two collection functions of Milestone 4: `$select` and `$map`. Each
function's contract follows the format required by [`Milestones.md`](Milestones.md): name, arguments,
argument types, return type, null behavior, collection behavior, error behavior, and provider requirements.

Both functions require an array target (`TargetRule.Required`) and evaluate their arguments once per
element, exactly like `$filter`'s predicate — `~`, plain fields, paths (`a.b`), and `$field` all work the
same way inside them as they do inside `$filter`. See [`FieldAccess.md`](FieldAccess.md) and
[`Variables.md`](Variables.md).

## `$map`

**Name:** `$map`
**Arguments:** one, unnamed — any expression, evaluated per element.
**Argument types:** any type.
**Return type:** `array<T>`, where `T` is the argument expression's type.
**Null behavior:** the expression may itself evaluate to `null` per element; `$map` does not special-case
it. A `null` result is written as `null` in the output array, in whatever position that element occupies.
**Collection behavior:** produces exactly one output element per input element, in the same order. An empty
input array gives an empty output array.
**Error behavior:** `INVALID_TARGET` if the target is not an array. Any error the argument expression itself
can raise (`TYPE_MISMATCH`, `UNKNOWN_IDENTIFIER`, and so on) is raised the same way it would inside `$filter`.
**Provider requirements:** a provider translates `$map` to its native projection (`SELECT` of a single
expression, LINQ `Select`). Element order must be preserved unless the query specifies otherwise (see
`$sort`, a later part of this milestone).

```
$source([1, 2, 3]).$map(~ gt 1)              -- [false, true, true]
$source([{id: 1, name: 'a'}]).$map(name)     -- ["a"]
```

`$map`'s argument may be an object literal, in which case it behaves exactly like `$select`'s object form
(see below) — the two are the same underlying mechanism.

## `$select`

**Name:** `$select`
**Arguments:** one or more, in one of two mutually exclusive forms.
**Return type:** always `array<object>`.

### List form: `$select(field1, field2, ...)`

Each argument must be a plain field reference: an identifier (`id`), or a path (`address.city`). Nothing
else is accepted — not `~`, not `$field`, not a literal, not a variable, and not an arbitrary expression —
because the output key is taken from the reference itself, and only these forms have one.

* **Output key:** the field's own name, or the last segment of a path (`address.city` becomes the key
  `city`). Two arguments that produce the same key — including two different paths ending in the same
  segment — are `DUPLICATE_FIELD`.
* **Argument types:** any type (whatever the referenced field's type is).
* **Null behavior:** a field that is missing on some rows is included in the shape and reads as `null` on
  those rows, the same as a plain field reference anywhere else.
* **Error behavior:** an argument that is not a field reference is `INVALID_SELECT_ARGUMENT`, with a message
  pointing at `$select({...})` as the alternative. A duplicate key is `DUPLICATE_FIELD`.

```
$source([{id: 1, name: 'a'}]).$select(id, name)         -- [{"id": 1, "name": "a"}]
$source([{id: 1, addr: {city: 'x'}}]).$select(id, addr.city)   -- [{"id": 1, "city": "x"}]
```

### Object form: `$select({key: value, ...})`

A single argument that is an object literal. Every entry's key becomes the output key (following the same
rules as any object literal — an identifier or a quoted string, see [`DataTypes.md`](DataTypes.md#objects));
every entry's value is any expression, evaluated per element exactly like `$filter`'s predicate.

* **Value expressions:** a field, a path, `~` (the whole element), a variable, `$field(...)`, a literal, or
  a nested object literal. There is no restriction beyond what any per-element expression already allows.
* **Duplicate keys** are rejected the same way as in any object literal (`DUPLICATE_FIELD`).
* **Error behavior:** whatever the value expressions themselves can raise.

```
$select({myId: id, label: name})
$select({row: ~})                          -- keep the whole element under one key
$select({name: name, city: addr.city})     -- rename while flattening a nested field
```

The list and object forms cannot be mixed in one call: `$select(id, {name: name})` is not a form this
function recognizes (the second argument, an object literal, is not a plain field reference), so it fails
as `INVALID_SELECT_ARGUMENT` under the list-form rule. Use the object form for the whole call instead:
`$select({id: id, name: name})`.

### Wildcard

`$select(*)` is reserved for a future version and does not parse in v1 (`*` is not a recognized character).

## Provider requirements (both forms)

A provider translates `$select` to its native projection (`SELECT` with named columns, LINQ anonymous-type
`Select`). Both forms produce the same kind of result (`array<object>`) and can be translated identically
once the analyzer has resolved each output key and its expression — the distinction between list and object
form exists only in the query's syntax, not in what a provider needs to generate.

## Chaining

Both functions return ordinary arrays and compose with everything else: `$filter` after `$select` sees the
*projected* shape, not the original row.

```
$source([{id: 1}, {id: 2}]).$select(id).$filter(id eq 1)   -- [{"id": 1}]
```

## `$sort`

**Name:** `$sort`
**Arguments:** 1 or 2. The first is a key expression (any per-element expression: a field, a path, `~`,
`$field(...)`). The second, optional, is the bare keyword `asc` or `desc` — not a string, not a variable,
not an expression evaluated against the element.
**Argument types:** the key must be `int`, `long`, `decimal`, `string`, or `any`. `bool`, `array`, and
`object` are rejected (`TYPE_MISMATCH`), the same set `gt`/`lt` accept (see
[`Operators.md`](Operators.md#comparison-operators)).
**Return type:** the same array type as the target — sorting never changes the row shape.
**Null behavior:** a `null` key always sorts as the smallest value, in both directions — ascending puts
`null` first, descending puts it last. This is a definite, testable position, not an "undefined" one.
**Collection behavior:** the sort is stable — elements whose key compares equal keep their original relative
order — in both directions. Default direction is ascending when the second argument is omitted.
**Error behavior:** `INVALID_ARGUMENT_COUNT` for anything other than 1 or 2 arguments. `INVALID_TARGET` if
the target is not an array. `INVALID_SORT_DIRECTION` if the second argument is present but is not literally
`asc` or `desc` (including a quoted `'desc'` or a variable).
**Provider requirements:** translates to `ORDER BY` / LINQ `OrderBy`/`OrderByDescending`, with nulls placed
using the provider's own `NULLS FIRST`/`NULLS LAST` (or equivalent) so that behavior matches exactly.

```
$source([{id: 3}, {id: 1}, {id: 2}]).$sort(id)             -- ascending by id
$source([{id: 3}, {id: 1}, {id: 2}]).$sort(id, desc)       -- descending by id
$source(['b', 'a', 'c']).$sort(~)                          -- scalar arrays sort too
```

Only one sort key is supported in v1. For a secondary tie-breaker, there is currently no way to express
"then by" — this is a known limitation, not an oversight, and may be revisited in a later version.

## `$take` and `$skip`

**Name:** `$take` / `$skip`
**Arguments:** one, unnamed — a count, evaluated once against the surrounding scope (not per element; it is
not affected by `UsesElementScope`, so it cannot reference `~` or a field, only a literal or a variable).
**Argument types:** `int` or `long`. A `decimal`, `string`, `bool`, or any other type is `TYPE_MISMATCH`.
**Return type:** the same array type as the target.
**Null behavior:** not applicable — the count itself is never `null` at the type level (no type unifies to
allow it here); this is enforced the same way any other typed argument is.
**Collection behavior:** `$take(n)` keeps the first `n` elements; `$skip(n)` discards the first `n` and keeps
the rest. A negative `n` is treated as `0` for both — following `System.Linq`'s own `Take`/`Skip` behavior —
so `$take(-1)` gives an empty array and `$skip(-1)` returns every element unchanged. A count larger than the
array's length is clamped: `$take` returns everything, `$skip` returns nothing.
**Error behavior:** `INVALID_TARGET` if the target is not an array.
**Provider requirements:** translates to `OFFSET`/`FETCH` (or `LIMIT`/`OFFSET`) and LINQ `Take`/`Skip`. Both
functions require a defined row order to be meaningful; a provider should require (or warn on) a `$sort`
earlier in the chain when translating to a query language without a guaranteed default order, such as SQL.
The in-memory provider does not enforce this, because its array order is always well-defined.

```
$source([{id: 1}, {id: 2}, {id: 3}]).$sort(id, desc).$take(2)   -- the two largest ids
$let(@pageSize, 10).$source(...).$sort(id).$take(@pageSize)
```

## `$count`

**Name:** `$count`
**Arguments:** none.
**Return type:** `long`.
**Null behavior:** not applicable.
**Collection behavior:** terminal — ends the chain (or a further scalar-only step) at a single number: the
number of elements in the target array. An empty array gives `0`.
**Error behavior:** `INVALID_TARGET` if the target is not an array. `INVALID_ARGUMENT_COUNT` if called with
any argument.
**Provider requirements:** translates to `COUNT(*)` / LINQ `Count()`.

```
$source([{id: 1}, {id: 2}, {id: 3}]).$count()   -- 3
$source([]).$count()                            -- 0
```

## `$any`

**Name:** `$any`
**Arguments:** 0 or 1 — an optional predicate, evaluated per element exactly like `$filter`'s.
**Return type:** `bool`.
**Null behavior:** not applicable; the result is always `true` or `false`, following the same two-valued
rules `$filter` already uses for its predicate.
**Collection behavior:** terminal. `$any()` (no predicate) is `true` when the array has at least one
element. `$any(predicate)` is `true` when at least one element satisfies it. An empty array is always
`false`.
**Error behavior:** `INVALID_TARGET` if the target is not an array. `INVALID_ARGUMENT_COUNT` for 2 or more
arguments. Any error the predicate itself can raise, same as `$filter`.
**Provider requirements:** translates to `EXISTS (...)` / LINQ `Any()`. A provider should stop scanning as
soon as a match is found, the same way the in-memory implementation does.

```
$source([{id: 1}, {id: 2}]).$any()             -- true
$source([]).$any()                             -- false
$source([{id: 1}, {id: 2}]).$any(id eq 5)      -- false
```

## `$first`

**Name:** `$first`
**Arguments:** 0 or 1 — an optional predicate, evaluated per element exactly like `$filter`'s.
**Return type:** the array's element type (not an array — a single value, or `null`).
**Null behavior:** if no element exists (empty array, or none match the predicate), the result is `null`,
not an error.
**Collection behavior:** terminal. Returns the first element (in the target's current order — apply `$sort`
first if a specific one matters) that satisfies the predicate, or the very first element if there is none.
**Error behavior:** `INVALID_TARGET` if the target is not an array. `INVALID_ARGUMENT_COUNT` for 2 or more
arguments. Any error the predicate itself can raise.
**Provider requirements:** translates to `TOP 1` / `LIMIT 1` / LINQ `FirstOrDefault()`. Because the result
can legitimately be `null` (either because the array was empty or because a row's own value is `null`),
a provider cannot use "returned null" to mean "not found" versus "found a null value" — if that distinction
ever matters to a caller, it needs `$any` or `$count` alongside `$first`, not `$first` alone.

```
$source([{id: 1}, {id: 2}]).$first()              -- {"id": 1}
$source([]).$first()                              -- null
$source([{id: 1}, {id: 2}]).$first(id eq 2)       -- {"id": 2}
```

## `$distinct`

**Name:** `$distinct`
**Arguments:** 0 or 1 — an optional key expression, evaluated per element exactly like `$filter`'s
predicate (though its result does not need to be a `bool`).
**Argument types:** any type.
**Return type:** the same array type as the target.
**Null behavior:** `null` is a value like any other for this purpose — two `null` keys are duplicates of
each other, and a `null` element itself is deduplicated normally.
**Collection behavior:** keeps the **first** element of every group of elements that share the same key
(or, without a key, the same whole value), in their original relative order. Comparison is **structural**,
not the `eq` operator: unlike `eq`, arrays and objects here are compared by their contents (an object is
equal to another with the same field names and values, in any order; a missing field is not equal to a
field present with value `null`). Numbers compare by value across types, exactly like `eq` (`1` and `1.0`
are duplicates); strings compare ordinally.
**Error behavior:** `INVALID_TARGET` if the target is not an array. `INVALID_ARGUMENT_COUNT` for 2 or more
arguments.
**Provider requirements:** translates to `SELECT DISTINCT` / `GROUP BY` / LINQ `Distinct()`/`DistinctBy()`.
A provider is not required to preserve "first occurrence" order the way the in-memory implementation does,
because SQL's `DISTINCT` does not guarantee it either — order after `$distinct` should be treated as
unspecified unless followed by `$sort`.

```
$source([1, 2, 1, 3, 2]).$distinct()                              -- [1, 2, 3]
$source([{g: 'a', n: 1}, {g: 'a', n: 2}, {g: 'b', n: 3}]).$distinct(g)  -- one row per g
```
