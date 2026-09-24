# FunQuery `~` and Field Paths

This document specifies `~` (the current element) and dotted field paths (`a.b`, `~.a.b`). Types are
defined in [`DataTypes.md`](DataTypes.md); variables and `$let` are specified in
[`Variables.md`](Variables.md).

## `~`: the current element

Inside a function that evaluates per element (`$filter`; later `$map`, `$sort`, and so on — see
`FunctionDefinition.UsesElementScope`), `~` refers to the element itself, as opposed to one of its fields.

```
$source([1, 2, 3]).$filter(~ gt 1)          -- [2, 3]
$source([{id: 1}, {id: 2}]).$filter(~.id eq 1)
```

* Outside an element-scoped function, `~` is `ITEM_OUT_OF_CONTEXT`. This includes the value expression of
  a `$let` that is not itself nested inside such a function: `$source([1]).$let(@x, ~)` fails, because
  `$let`'s value is evaluated in the surrounding scope, which has no current element there.
* `~` may be the whole predicate, exactly like a field: `$filter(~)` keeps every truthy element.
* `~` participates in every operator the same way a field does, subject to the usual type rules. Comparing
  `~ eq ~` is only valid for scalar elements (numbers, strings, booleans), because arrays and objects still
  cannot be compared with any operator (see [`Operators.md`](Operators.md#comparison-operators)).

### `$filter` now accepts arrays of any element type

Before `~` existed, `$filter` (and any other `UsesElementScope` function) required an array of objects,
because there was no way to refer to a non-object element. That requirement is gone: `$filter`'s target may
be an array of any type.

* When the element type is an object, its fields are available as plain identifiers, exactly as before.
* When the element type is not an object (a number, a string, a bool, or `unknown`), there are no named
  fields — a bare identifier is `UNKNOWN_IDENTIFIER` — and `~` is the only way to refer to the element.
* `$filter` still requires an array. A non-array target is `INVALID_TARGET`, unchanged.

### Filtering an empty array

`$source([]).$filter(...)` now succeeds when the predicate does not need to know the element's type:

```
$source([]).$filter(true)     -- []
$source([]).$filter(~ eq 1)   -- TYPE_MISMATCH: cannot compare unknown with int
```

An empty array's element type is `unknown` (see [`DataTypes.md`](DataTypes.md#internal-types)). A predicate
that references `~` or a field still fails to type-check against `unknown`, which is correct: there is
nothing to say the comparison would ever have been valid. A predicate that does not depend on the element at
all is unaffected, and the result is simply an empty array.

## Field paths: `a.b`, `~.a.b`

A `.` immediately followed by an identifier continues a path into a nested object field, starting from a
plain field, from `~`, or from another path:

```
$filter(address.city eq 'Jakarta')
$filter(~.address.city eq 'Jakarta')       -- same field, reached through ~
$filter(address.geo.lat gt 0)              -- any depth
```

* `address.city` and `~.address.city` name the same value; `~.field` is never required, only more explicit.
* A path element must resolve to an object at each step. Accessing a field on a non-object value —
  `id.x` where `id` is a number — is `TYPE_MISMATCH` (`Cannot access field 'x' on int.`), pointing at the
  value the field was looked up on, not at the whole path.
* A field that does not exist on the (statically known) object shape is `UNKNOWN_IDENTIFIER` (`Unknown
  field 'name'.`), the same code as an unknown top-level identifier, so provider code and clients only need
  to handle one "no such field" case.
* If the *root* of a path does not exist at all — `addr.city` where the row has no `addr` field — the error
  is reported on the root (`Unknown identifier 'addr'.`), before the `.city` part is even considered.

### Grammar note: three roles of `.`

As specified informally elsewhere in the language, `.` plays exactly three roles, and the token that follows
it decides which:

| What follows `.` | Role | Handled by |
| --- | --- | --- |
| A digit, directly after a digit | Part of a decimal number (`1.5`) | Lexer (never emits a separate `Dot` token here) |
| `$name` | Chains a function call (`).$filter(...)`) | `ParsePostfix`, and only when the left side is already a `CallExpression` |
| An identifier | Continues a field path (`a.b`) | `ParsePath`, applied right after parsing an identifier or `~` |

A `.` that does not fit one of these — for example after a call (`$source(...).x`, an identifier where a
field name is expected) or after a plain field reference where a call was expected
(`$filter(a).$nope()`, a call chained onto something that is not itself a call) — is `UNEXPECTED_TOKEN` or
`EXPRESSION_OUTSIDE_FUNCTION`, exactly as an unrecognized token would be anywhere else.

## `$field(name)`: field access for names plain syntax cannot express

`address.city` cannot name every field: some names are reserved words (`in`, `not`, ...), contain
characters outside the identifier rules (`first-name`), or are only known at query time. `$field` covers
all three cases.

```
$field('in')                 -- a field literally named "in"
$field('address.city')       -- a dotted path, exactly like address.city
$field(@column)              -- the field named by whatever @column holds
```

* The argument must be a string literal or a variable — nothing else. `$field(a)`, `$field(1)`, and
  `$field(a.b)` are all `INVALID_FIELD_ARGUMENT`.
* `$field` reads from the current element, exactly like a bare identifier or `~`. It cannot be called on a
  target: `row.$field('x')` is `INVALID_TARGET` (`must start the chain`), for the same reason `$source`
  cannot — but a leading `$let` chain is transparent to it, just as it is to `$source`.
* Used outside an element-scoped function, `$field` is `ITEM_OUT_OF_CONTEXT`, the same as `~`.

### String literal: resolved at analysis time

`$field('address.city')` is resolved exactly like the equivalent path syntax, segment by segment, against
the current element's (statically known) type. The same errors apply: `TYPE_MISMATCH` if a segment's target
isn't an object, `UNKNOWN_IDENTIFIER` if a field or the path's root doesn't exist. An empty segment (`''`,
`'a.'`, `'.a'`, `'a..b'`) is `INVALID_FIELD_ARGUMENT`.

### Variable: resolved at execution time

`$field(@column)` cannot be type-checked in advance — the field name is only known once `@column`'s value
is read. The analyzer only checks that `@column` itself is defined, and gives the call type `any`, the same
type used elsewhere for values a provider can only describe at runtime.

* The variable is read once, when the query is compiled — not once per row — consistent with how every
  other variable is used (see [`Variables.md`](Variables.md#evaluation)).
* Its value must be a string (a plain name or a dotted path). Anything else is `INVALID_FIELD_ARGUMENT`,
  raised at that point rather than during analysis, because the analyzer cannot see the value.
* Unlike the string-literal form, a missing field or a non-object intermediate is not an error at
  execution time — it simply reads as null, the same way any other missing field does. There is no static
  type to check the path against, so there is nothing to reject in advance.

## Error reference

| Situation | Code |
| --- | --- |
| `~` used outside an element-scoped function | `ITEM_OUT_OF_CONTEXT` |
| A field access step's target is not an object | `TYPE_MISMATCH` |
| A field named in a path does not exist on its (object) target | `UNKNOWN_IDENTIFIER` |
| The root identifier of a path does not exist at all | `UNKNOWN_IDENTIFIER` |
| `$field`'s argument is not a string literal or a variable | `INVALID_FIELD_ARGUMENT` |
| `$field`'s string literal is an empty or malformed path | `INVALID_FIELD_ARGUMENT` |
| `$field(@variable)`'s bound value is not a string, found at execution time | `INVALID_FIELD_ARGUMENT` |
| `$field` called on a target | `INVALID_TARGET` |

## What is deliberately out of scope for this version

* **Paths through a variable** (`@x.field`). Variables are opaque values in v1; introspecting into a bound
  object through a path may be added later without changing anything specified here.
* **`~~` (the parent element)**, for nested collections — not needed until collections can appear inside
  other collections' predicates, which is a later extension.
* **Paths after `$field(@variable)`'s own result** (`$field(@x).y`). `$field`'s result is always a leaf
  value in v1; chaining a further path onto it may be added later without changing anything specified here.
