# FunQuery Data Types

This document defines the value types of the language: how each type is written, how it is inferred, and
how types relate to each other in comparisons. Operators are specified in [`Operators.md`](Operators.md).

Type names below are the names used in error messages (`Cannot compare string with int.`).

## Overview

| Type      | Literal                    | In-memory representation        | Status |
| --------- | -------------------------- | ------------------------------- | ------ |
| `int`     | `42`                       | `System.Int32`                  | v1     |
| `long`    | `2147483648`               | `System.Int64`                  | v1     |
| `decimal` | `1.5`                      | `System.Decimal`                | v1     |
| `string`  | `'hello'`                  | `System.String`                 | v1     |
| `bool`    | `true`, `false`            | `System.Boolean`                | v1     |
| `null`    | `null`                     | absence of a value              | v1     |
| `array`   | `[1, 2, 3]`                | ordered list of one element type| v1     |
| `object`  | `{id: 1, name: 'x'}`       | ordered set of named fields     | v1     |

`true`, `false`, and `null` are reserved words. They cannot be used as field names, and they are
case-sensitive: `TRUE` and `Null` are ordinary identifiers.

`float` and `double` are **not language types in v1**. See [Floating point](#floating-point).

## Numbers

The language has three numeric types: `int`, `long`, and `decimal`. There is a single kind of numeric
literal. Its type is decided by the semantic analyzer from the written value, never from a suffix.

### Literal syntax

A number is one or more digits, optionally followed by a `.` and one or more digits.

| Written         | Valid | Notes                                              |
| --------------- | ----- | -------------------------------------------------- |
| `42`, `007`     | yes   | leading zeros are allowed; `007` is `7`            |
| `1.5`, `0.10`   | yes   |                                                    |
| `1.`, `.5`      | no    | write `1.0` and `0.5`                              |
| `1e5`, `2m`, `1.5f` | no | no exponent and no type suffix                    |
| `+1`, `-1`      | no    | negative numbers arrive in Milestone 3             |

A `.` belongs to the number only when a digit follows it, so `$take(1).$filter(...)` is a chain and not a
decimal.

### Type inference

| Literal                                          | Type      |
| ------------------------------------------------ | --------- |
| digits only, fits in a 32-bit signed integer     | `int`     |
| digits only, fits in a 64-bit signed integer     | `long`    |
| digits only, larger than `long` but fits `decimal` | `decimal` |
| has a decimal point and fits `decimal`           | `decimal` |
| does not fit `decimal`                           | error `NUMBER_OUT_OF_RANGE` |

Examples: `2147483647` is `int`, `2147483648` is `long`, `9223372036854775807` is `long`,
`99999999999999999999` is `decimal`, `1.5` is `decimal`.

### Numeric comparison across types

`int`, `long`, and `decimal` can be compared with each other. The comparison is by numeric value, as if
both sides were `decimal`, which is lossless for all three types. `1.0 eq 1` is true. The scale of a
decimal (the number of trailing zeros) has no effect.

This applies to comparison only. Elements of an array must currently have the same type (see
[Arrays](#arrays)).

## Floating point

`float` and `double` are not part of the v1 language:

* no literal produces them;
* a field can only have them when a provider schema declares them.

The language may still meet them when a data source or provider (for example an EF model) exposes columns of
those types. The decision for that case is fixed now, so that no provider can expose a query surface with
unreliable equality:

> **`eq` and `neq` are rejected when either operand is `float` or `double`.**
> `gt`, `gte`, `lt`, and `lte` are allowed.

Reason: two floating point values that "should" be equal often differ in the last bits, and the same
literal can be stored differently by an in-memory `double`, a LINQ provider, and a SQL `float` column. An
equality test then gives different answers in different providers. A range comparison degrades gracefully.

The error is `TYPE_MISMATCH` with a message that suggests a range comparison. This rule is enforced by the
semantic analyzer and covered by tests. It applies equally to in-memory, LINQ, and SQL execution.

How a provider maps its schema types to language types is decided when providers are introduced
(Milestones 5 and 6). Until then `float` and `double` are not available in queries.

## Strings

* Written between single quotes: `'hello'`. A string may contain spaces and any Unicode character.
* Double quotes are not string delimiters.
* Escaping a single quote inside a string (`'it''s'`) arrives in Milestone 3.
* Comparison is **ordinal and case-sensitive**: `'a' eq 'A'` is false, and ordering compares UTF-16 code
  units. It never depends on the culture of the server.
* A string and a number are never compared. `name gt 1` is `TYPE_MISMATCH`.

## Booleans

* Produced by comparisons and logical operators, and written as the literals `true` and `false`.
* Booleans can be tested for equality (`eq`, `neq`) but have no order: `gt`, `gte`, `lt`, `lte` are rejected.
* The operands of `and` and `or`, and the predicate of `$filter`, must be `bool`. A number is not truthy.

## Null

These rules are the reference that every provider must reproduce.

* A field that is missing from an object reads as `null`.
* `null` has its own type. It can be compared with any scalar using `eq` and `neq`.
* `x eq null` is true when `x` is null, and `x neq null` is true when `x` is not null.
* The semantics are **two-valued**: a comparison is always true or false, never "unknown". In particular
  `null eq 5` is false and `null neq 5` is true.
* Ordering a null value (`gt`, `gte`, `lt`, `lte`) is false. Ordering against the `null` literal is a type
  error, because `null` has no order.
* Inside arrays, `null` unifies with any other type. `[1, null, 2]` is an array of `int`, and
  `[{id: 1}, {id: null}]` is an array of objects whose `id` is an `int`. A field that is null in every row has
  type `null`, and cannot be ordered.
* A `bool` that is null counts as false where a boolean is required: as an operand of `and` and `or`, and as
  the predicate of `$filter`.

SQL uses three-valued logic. A SQL provider must translate to the two-valued behavior above (for example
`x neq @p` becomes `(x <> @p OR x IS NULL)`). The language does not adopt SQL's behavior.

## Arrays

* Written `[a, b, c]`. Trailing commas are rejected. `[]` is allowed.
* All elements must currently have the same type. `[1, 'a']` is `INCOMPATIBLE_ELEMENT_TYPES`, and so is
  `[1, 1.5]`, because the element types `int` and `decimal` differ. Numeric widening and heterogeneous
  arrays arrive in Milestone 3. `null` is the exception: it unifies with any element type (see
  [Null](#null)). An empty array unifies with any array, so `[[], [1]]` is an array of `array<int>`.
* An array is a value, but it cannot be compared with any operator (see [`Operators.md`](Operators.md)).

## Objects

* Written `{name: value, ...}`. Every entry must have the form `name: value`. Trailing commas are rejected.
  `{}` is allowed and is an object without fields.
* Field names are identifiers: ASCII letters, digits, and `_`, not starting with a digit. They are
  case-sensitive.
* Duplicate names in one object are rejected (`DUPLICATE_FIELD`).
* In an array of objects, every object must currently have the same set of fields with compatible types.
  Field order does not matter. Sources with missing fields arrive in Milestone 3.
* An object cannot be compared with any operator.

## Internal types

These types appear in the analyzer but cannot be written in a query.

| Type      | Meaning                                                                    |
| --------- | -------------------------------------------------------------------------- |
| `any`     | a value whose type is only known at runtime (for example from a provider). Comparisons with it are accepted |
| `unknown` | the element type of an empty array `[]`                                    |
| `void`    | reserved                                                                   |

## Compatibility summary

Two types can be compared when they are the same type, when both are numeric, or when one of them is `any`.
Whether the comparison is then allowed depends on the operator (see [`Operators.md`](Operators.md)).

| Left \ Right | int / long / decimal | string | bool | array / object |
| ------------- | -------------------- | ------ | ---- | -------------- |
| int / long / decimal | yes           | no     | no   | no             |
| string        | no                   | yes    | no   | no             |
| bool          | no                   | no     | yes  | no             |
| array / object | no                  | no     | no   | no             |

There is no implicit conversion between a number and a string, or between a number and a bool.

## Provider expectations

A provider translates language types to the types of its data source. It must preserve the language
semantics above rather than the native semantics of the source.

| Language type | Typical source types                 | Notes |
| ------------- | ------------------------------------ | ----- |
| `int`         | `smallint`, `int`                    |       |
| `long`        | `bigint`                             |       |
| `decimal`     | `decimal`, `numeric`, `money`        | compare by value, not by scale |
| `string`      | `nvarchar`, `varchar`, `text`        | ordinal, case-sensitive (see [Operators](Operators.md#strings)) |
| `bool`        | `bit`, `boolean`                     |       |
| not available | `float`, `real`, `double precision`  | see [Floating point](#floating-point) |

The types of the source columns come from the source registry (Milestone 5). A literal is sent as a typed
parameter whose type follows the column, never by casting the column.
