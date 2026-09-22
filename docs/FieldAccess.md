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

## Error reference

| Situation | Code |
| --- | --- |
| `~` used outside an element-scoped function | `ITEM_OUT_OF_CONTEXT` |
| A field access step's target is not an object | `TYPE_MISMATCH` |
| A field named in a path does not exist on its (object) target | `UNKNOWN_IDENTIFIER` |
| The root identifier of a path does not exist at all | `UNKNOWN_IDENTIFIER` |

## What is deliberately out of scope for this version

* **Paths through a variable** (`@x.field`). Variables are opaque values in v1; introspecting into a bound
  object through a path may be added later without changing anything specified here.
* **`~~` (the parent element)**, for nested collections — not needed until collections can appear inside
  other collections' predicates, which is a later extension.
* **`$field`**, for field names that are not valid identifiers or that are chosen dynamically (via a
  variable). It reuses the object-field lookup described here but is specified separately.
