# FunQuery Variables

This document specifies `@` variables and `$let`: how they are declared, how they scope, and how they
interact with the rest of the chain. Types are defined in [`DataTypes.md`](DataTypes.md); the general rule
that every expression must live inside a function call is defined in the language grammar and is assumed
here.

## Syntax

```
$let(@name, value)
```

* `@name` is a variable reference. The `@` is part of the token; `name` follows identifier rules (ASCII
  letters, digits, `_`, not starting with a digit).
* `$let` takes exactly two arguments. The first **must** be a bare `@name` — a declaration, not a reference.
  Anything else (`$let(1, 2)`, `$let('x', 2)`, `$let(id, 2)`) is `INVALID_LET_TARGET`.
* `value` is evaluated once, in the environment at that point in the chain (see [Evaluation](#evaluation)).
* `$let` does not filter, project, or otherwise change the data flowing through the chain. It passes its
  target through unchanged and makes `@name` available to every step that follows it.

```
$let(@min, 18)
    .$source([{age: 17}, {age: 18}, {age: 25}])
    .$filter(age gte @min)
```

## `$let` is transparent to the chain

`$let` may appear **anywhere** in a chain — before the data source, after it, or between other steps — and
in any number:

```
$let(@a, 1).$let(@b, 2).$source(...).$filter(...)
$source(...).$let(@x, 1).$filter(...)
```

For the purpose of target-rule checks (a function that must, may, or must not be called on a target — see
`FunctionDefinition.TargetRule`), a chain of `$let` calls does not count as "having a target": it is
skipped over when deciding whether a function is being called on real data.

* `$let(@x, 1).$source(...)` is valid: `$source` requires no target, and the only thing preceding it is
  `$let`, which does not count.
* `$source(...).$let(@x, 1).$source(...)` is still rejected (`INVALID_TARGET`): `$source` still cannot
  follow real data, even through a `$let`.
* `$let(@x, 1).$filter(...)` fails, because after skipping `$let` there is no array for `$filter` to work
  on — this is a genuine error, not a `$let` limitation.

Providers and future functions with `TargetRule.Forbidden` (functions that must start a chain, like
`$source`) must apply the same rule: a leading `$let` chain does not count as a target, but a `$let` chain
must still be evaluated for its side effect (the binding) even though its value is discarded. The in-memory
provider does this in `CoreInMemoryFunctions.Source`; any future `Forbidden`-target function's
implementation must do the same.

## Scope

A variable is visible to every step **after** the `$let` that declares it, for the rest of that chain.
There is no way to "unset" a variable, and scope only ever grows forward.

### Nested `$let` inside a value does not leak

The `value` argument of `$let` is itself an ordinary expression and may contain further `$let` calls (for
example when it is a sub-query: `$let(@x, $let(@y, 1).$source(...))`). Any variable bound while evaluating
that value is discarded once the value has been computed — it is not visible after the outer `$let`:

```
$let(@x, $let(@y, 1).$source([{n: @y}]))
    .$source(@x)
    .$filter(n eq @y)   -- UNDEFINED_VARIABLE: @y only existed while computing @x's value
```

This also means a variable declared inside a nested value can reuse a name already used outside it, without
conflict, because the two scopes never overlap:

```
$let(@x, $let(@x, 1).$source([{n: @x}]))   -- fine: the inner @x is discarded before the outer @x is bound
    .$source([{m: 1}])
```

### Redefinition

Declaring the same name twice in the same visible scope is an error, regardless of the values:

```
$let(@x, 1).$let(@x, 2)...   -- VARIABLE_REDEFINED, pointing at the second @x
```

## Evaluation

* `value` is evaluated **once**, not once per row. `$let(@min, 18)` computes `18` a single time, no matter
  how many rows the query later produces.
* `value` may be a full sub-chain, for example `$let(@x, $source(...).$filter(...))`. It is evaluated with
  no "current row" — it cannot use `$let`'s own outer context's element scope, only fields and variables
  that are otherwise in scope at that point (for instance, if `$let` sits inside `$filter`'s predicate, the
  value can see that predicate's element fields).
* The resulting value is **materialized**: a computed value, not a query that gets re-run. This matches the
  language's general rule that a variable holds a value, never a function.
* A provider is free to implement this differently (for example, translating a `$let` value into a SQL CTE
  or a query parameter) as long as the value behaves as if computed once up front.

## `$source(@variable)`

A variable bound to an array can be used as `$source`'s argument, exactly like an inline array literal:

```
$let(@items, [{id: 1}, {id: 2}])
    .$source(@items)
    .$filter(id eq 2)
```

* The variable must resolve to an `array` type. Anything else is `TYPE_MISMATCH` (`Argument 1 of '$source'
  expects array, got string.`), the same error `$source` would give for any other wrong-typed argument.
* An undefined variable is `UNDEFINED_VARIABLE`, not `TYPE_MISMATCH` — the two are reported separately so
  that "the variable doesn't exist" and "the variable is the wrong type" are never confused.
* Variables from outside the query text (bound by the HTTP layer from query parameters, `&@min=18`) are out
  of scope for this document — see [Milestone 5](Milestones.md#milestone-5--aspnet-core-integration). This
  document covers only variables declared with `$let` inside the query itself. `QueryEngine` may grow an
  overload to accept externally supplied variables once that milestone defines how their literal text is
  parsed and validated.

## Error reference

| Situation | Code |
| --- | --- |
| First argument of `$let` is not a bare `@name` | `INVALID_LET_TARGET` |
| `$let` called with a number of arguments other than 2 | `INVALID_ARGUMENT_COUNT` |
| `@name` referenced but never bound in the visible scope | `UNDEFINED_VARIABLE` |
| `@name` declared twice in the same visible scope | `VARIABLE_REDEFINED` |
| `$source(@name)` where `@name` is not an array | `TYPE_MISMATCH` |
| A function with `TargetRule.Forbidden` is called after real data, even through `$let` | `INVALID_TARGET` |

## What is deliberately out of scope for this version

* **Reading a variable's value back out of a row.** Without `$select` (Milestone 4), there is no way to
  project `@x` into the output on its own; it can only be used inside a predicate (`$filter`) or as
  `$source`'s argument.
* **`~` (the current element) and field paths (`a.b`)**, which are specified together with `$field` in a
  later part of Milestone 3.
* **Binding variables from HTTP query parameters.** That is Milestone 5's responsibility; this document
  only covers `@` and `$let` as they appear inside the query text.
