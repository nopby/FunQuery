# FunQuery Operators

This document specifies every operator: its syntax, the operand types it accepts, its result type, and how
it treats `null` and numbers. Types are defined in [`DataTypes.md`](DataTypes.md).

All operators are lowercase words. Symbols such as `==`, `>`, `&&`, `+`, or `%` are never used, because
they have special meaning in URLs. Operator words are case-sensitive: `EQ` and `AND` are ordinary
identifiers.

## Overview

| Operator     | Meaning                    | Operands          | Result | Status |
| ------------ | -------------------------- | ----------------- | ------ | ------ |
| `eq`         | equal                      | two scalars       | `bool` | v1     |
| `neq`        | not equal                  | two scalars       | `bool` | v1     |
| `gt`         | greater than               | two ordered values| `bool` | v1     |
| `gte`        | greater than or equal      | two ordered values| `bool` | v1     |
| `lt`         | less than                  | two ordered values| `bool` | v1     |
| `lte`        | less than or equal         | two ordered values| `bool` | v1     |
| `and`        | logical and                | two `bool`        | `bool` | v1     |
| `or`         | logical or                 | two `bool`        | `bool` | v1     |
| `not`        | logical not                | one `bool`        | `bool` | M3     |
| `in`         | membership                 | scalar, array     | `bool` | M3     |
| `contains`   | substring test             | two `string`      | `bool` | M3     |
| `startswith` | prefix test                | two `string`      | `bool` | M3     |
| `endswith`   | suffix test                | two `string`      | `bool` | M3     |

A *scalar* is a number, string, or bool. An *ordered value* is a number or a string.

Operators marked M3 are specified here so that providers can plan for them. They are not implemented yet, and the
analyzer rejects them until then.

## Precedence and grouping

From tightest to loosest:

| Level | Operators                     | Associativity |
| ----- | ----------------------------- | ------------- |
| 1     | `( ... )` grouping            |               |
| 2     | `not` (M3)                    | prefix        |
| 3     | `eq neq gt gte lt lte`        | not chainable |
| 4     | `and`                         | left          |
| 5     | `or`                          | left          |

* `a eq 1 or b eq 2 and c eq 3` means `a eq 1 or (b eq 2 and c eq 3)`.
* Comparison operators do not chain. `id eq 1 eq 2` is a syntax error (`UNEXPECTED_TOKEN`). Use `and`.
* There is no guaranteed evaluation order and no guaranteed short-circuit. Operands never have side effects,
  so a provider may reorder or skip evaluation.

## Comparison operators

`eq`, `neq`, `gt`, `gte`, `lt`, `lte`.

**Result:** always `bool`. A comparison never fails at runtime because of the values, and never yields
"unknown" (see [null behavior](#null-behavior)).

### Operand rules

Both operands are checked together. The first rule that fails decides the error.

| # | Rule                                                                 | Error            |
| - | -------------------------------------------------------------------- | ---------------- |
| 1 | The two types must be comparable: the same type, both numeric, or one is `any` | `TYPE_MISMATCH` (`Cannot compare X with Y.`) |
| 2 | Neither operand is an array or an object                             | `TYPE_MISMATCH` (`Operator 'eq' cannot be applied to array.`) |
| 3 | For `eq` and `neq`: neither operand is `float` or `double`           | `TYPE_MISMATCH` (`... not supported for float and double.`) |
| 4 | For `gt`, `gte`, `lt`, `lte`: neither operand is `bool`              | `TYPE_MISMATCH` (`Operator 'gt' cannot be applied to bool.`) |

The resulting matrix:

| Operand types                        | `eq`, `neq` | `gt`, `gte`, `lt`, `lte` |
| ------------------------------------ | ----------- | ------------------------ |
| `int`, `long`, `decimal` (any mix)   | yes         | yes                      |
| `string`, `string`                   | yes         | yes                      |
| `bool`, `bool`                       | yes         | no                       |
| `float` or `double` (with any number)| **no**      | yes                      |
| `array` or `object`                  | no          | no                       |
| different families (number and string, string and bool, ...) | no | no          |

Comparing two literals (`1 eq 1`) is accepted.

### Numeric behavior

* Numbers of different types compare by value as if both were `decimal`. `id eq 1.0` with `id` of type
  `int` is true when `id` is `1`.
* `decimal` values compare by value, not by scale: `1.0 eq 1` is true.
* There is no arithmetic in v1, so there is no overflow.
* **`float` and `double`:** `eq` and `neq` are rejected. `gt`, `gte`, `lt`, `lte` are allowed. See
  [Floating point](DataTypes.md#floating-point) for the reason. The rule is identical in every provider.

### Strings

* Comparison is ordinal and case-sensitive, and does not depend on the culture of the server.
* Ordering compares UTF-16 code units, so `'B' lt 'a'` is true.
* For case-insensitive matching, use a function such as `$lower(name) eq 'a'` (Milestone 4).

### Null behavior

Applies from Milestone 2. Comparisons are **two-valued**.

| Expression          | `x` is null | `x` is not null            |
| ------------------- | ----------- | -------------------------- |
| `x eq null`         | true        | false                      |
| `x neq null`        | false       | true                       |
| `x eq 5`            | false       | `x = 5`                    |
| `x neq 5`           | **true**    | `x <> 5`                   |
| `x gt 5`, `x lte 5` (any ordering) | false | ordinary ordering |

* Ordering against the `null` literal (`x gt null`) is a type error, because `null` has no order.
* A missing field reads as `null`.

### Provider expectations

| Topic          | Requirement |
| -------------- | ----------- |
| Values         | Literals become typed parameters. The parameter type follows the column type from the source schema. The column is never cast, so indexes remain usable |
| Range          | A literal outside the range of the column type is an error (`NUMBER_OUT_OF_RANGE`), not an empty result |
| Strings        | Ordinal, case-sensitive. A SQL provider must use a binary collation for the comparison (for example `Latin1_General_BIN2` on SQL Server, `"C"` on PostgreSQL). If the database cannot, the provider must declare the limitation in its capabilities instead of silently using another collation |
| Null           | Translate to two-valued logic. `x neq @p` becomes `(x <> @p OR x IS NULL)`, `x eq null` becomes `x IS NULL`, and orderings must not match null rows |
| `float` / `double` | Never provide `eq` and `neq`. Do not reintroduce them through a cast |
| Unsupported    | A provider that cannot translate an operator fails with a clear error. It never falls back to evaluating in memory |

## Logical operators

`and`, `or`, and (Milestone 3) `not`.

* Operands must be `bool`. Anything else is `TYPE_MISMATCH` (`Left side of logical expression must be Boolean.`).
* `and` binds tighter than `or`. Both are left-associative.
* Because comparisons are two-valued, `and`, `or`, and `not` are two-valued too. There is no "unknown".

## Planned operators (Milestone 3)

| Operator     | Operands            | Behavior |
| ------------ | ------------------- | -------- |
| `not`        | `bool`              | negation. `not id eq 1` means `not (id eq 1)` |
| `in`         | scalar, array       | true if the scalar equals any element under `eq`. The element type must be comparable with the scalar. A null scalar is true only if the array contains null. `float` and `double` are rejected, as for `eq` |
| `contains`   | `string`, `string`  | ordinal, case-sensitive substring test |
| `startswith` | `string`, `string`  | ordinal, case-sensitive prefix test |
| `endswith`   | `string`, `string`  | ordinal, case-sensitive suffix test |

For `contains`, `startswith`, and `endswith`, a null operand gives false. Providers that use `LIKE` must
escape the wildcard characters (`%`, `_`, and the escape character) in the operand, so that the text is
matched literally.

## Error reference

| Situation                                               | Code                   |
| ------------------------------------------------------- | ---------------------- |
| Types cannot be compared, or an operator does not accept the operand types | `TYPE_MISMATCH` |
| `id eq 1 eq 2`, or a misspelled operator such as `EQ`   | `UNEXPECTED_TOKEN`     |
| A number that does not fit `decimal`                    | `NUMBER_OUT_OF_RANGE`  |
