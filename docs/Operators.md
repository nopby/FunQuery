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
| `not`        | logical not                | one `bool`        | `bool` | v1     |
| `in`         | membership                 | scalar, array     | `bool` | v1     |
| `contains`   | substring test             | two `string`      | `bool` | v1     |
| `startswith` | prefix test                | two `string`      | `bool` | v1     |
| `endswith`   | suffix test                | two `string`      | `bool` | v1     |

A *scalar* is a number, string, or bool. An *ordered value* is a number or a string.

## Precedence and grouping

From tightest to loosest:

| Level | Operators                     | Associativity |
| ----- | ----------------------------- | ------------- |
| 1     | `( ... )` grouping            |               |
| 2     | `eq neq gt gte lt lte in contains startswith endswith` | not chainable |
| 3     | `not`                         | prefix        |
| 4     | `and`                         | left          |
| 5     | `or`                          | left          |

* `not` applies to a whole comparison, so `not id eq 1` means `not (id eq 1)`. It binds tighter than `and`:
  `not a eq 1 and b eq 2` means `(not a eq 1) and (b eq 2)`. `not` may be repeated (`not not a eq 1`).
* `a eq 1 or b eq 2 and c eq 3` means `a eq 1 or (b eq 2 and c eq 3)`.
* Comparison operators do not chain. `id eq 1 eq 2` and `name contains 'a' contains 'b'` are syntax errors
  (`UNEXPECTED_TOKEN`). Use `and`.
* `not` cannot be an operand of a comparison: `id eq not 1` is a syntax error. Use parentheses:
  `(not a eq 1) eq true`.
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
| 3 | For `eq` and `neq`: neither operand is `float` or `double`, unless the other operand is the `null` literal (a null test is not a value comparison) | `TYPE_MISMATCH` (`... not supported for float and double.`) |
| 4 | For `gt`, `gte`, `lt`, `lte`: neither operand is `bool` or the `null` literal | `TYPE_MISMATCH` (`Operator 'gt' cannot be applied to bool.`, `... to null.`) |

The resulting matrix:

| Operand types                        | `eq`, `neq` | `gt`, `gte`, `lt`, `lte` |
| ------------------------------------ | ----------- | ------------------------ |
| `int`, `long`, `decimal` (any mix)   | yes         | yes                      |
| `string`, `string`                   | yes         | yes                      |
| `bool`, `bool`                       | yes         | no                       |
| `float` or `double` (with any number)| **no** (`x eq null` and `x neq null` are allowed) | yes |
| the `null` literal (with any scalar) | yes         | no                       |
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

Comparisons are **two-valued**.

| Expression          | `x` is null | `x` is not null            |
| ------------------- | ----------- | -------------------------- |
| `x eq null`         | true        | false                      |
| `x neq null`        | false       | true                       |
| `x eq 5`            | false       | `x = 5`                    |
| `x neq 5`           | **true**    | `x <> 5`                   |
| `x gt 5`, `x lte 5` (any ordering) | false | ordinary ordering |

* Ordering against the `null` literal (`x gt null`) is a type error, because `null` has no order.
* A missing field reads as `null`.
* A field whose value is null in every row has type `null`. It can be tested with `eq` and `neq`, but it
  cannot be ordered, because its type carries no order.

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

`and`, `or`, and `not`.

* Operands must be `bool`. Anything else is `TYPE_MISMATCH` (`Left side of logical expression must be Boolean.`).
* `and` binds tighter than `or`. Both are left-associative.
* Because comparisons are two-valued, `and`, `or`, and `not` are two-valued too. There is no "unknown".
* A `bool` value that is null counts as false, both as an operand of `and` and `or` and as the predicate of
  `$filter`.

## Negation, membership, and text operators

### `not`

* Operand: `bool`. Anything else is `TYPE_MISMATCH` (`Operand of 'not' must be Boolean, got int.`), pointing at
  the operand.
* A null `bool` counts as false, so `not` of a null boolean is true.
* Provider: `not` must keep two-valued logic. Translate the operand so that it can never be "unknown" before
  negating it, for example by treating a null boolean as false.

### `in`

`value in [a, b, c]` is true when `value` equals one of the elements under `eq`. The right side may be an array
literal or an array field of the row: `'a' in tags`.

| Rule | Detail |
| ---- | ------ |
| Left operand | a scalar (number, string, bool, or `null`). An array or object is `TYPE_MISMATCH` |
| Right operand | an array, or `any`. Anything else is `TYPE_MISMATCH` (`needs an array on the right side`) |
| Element type | must be a scalar and comparable with the left operand under `eq`: numbers mix, other types must match. `[]` has no element type, so `x in []` is valid and always false |
| Numbers | compared by value, as for `eq`: `id in [1.0, 2.5]` matches `id` of `1` |
| Null | follows `eq`: `null in [null]` is true, `x in [1, null]` is true for a null `x`, and `x in [1]` is false for a null `x` |
| `float`, `double` | rejected, because `in` tests equality. Testing against `null` is allowed |
| Result | `bool`. Evaluates to false when the right side is null |

Provider: `in` translates to an `IN (...)` list, or to `IN` over a subquery when the right side is another
source. Null members need `IS NULL` alongside the list, because SQL `IN` never matches null. An empty list must
give false, and SQL does not allow `IN ()`, so the provider must emit a constant false.

### `contains`, `startswith`, `endswith`

`text contains part`, `text startswith prefix`, and `text endswith suffix` test a string.

| Rule | Detail |
| ---- | ------ |
| Operands | both `string` (or `null`, or `any`). A number, bool, array, or object is `TYPE_MISMATCH` (`requires string operands, got int`), pointing at the offending operand |
| Comparison | ordinal and case-sensitive, never dependent on the culture of the server |
| Empty text | every string contains, starts with, and ends with `''` |
| Null | a null operand gives false, never an error. `not name contains 'x'` is therefore true when `name` is null |
| Result | `bool` |

Provider: a provider that translates to `LIKE` must escape the wildcard characters (`%`, `_`, and the escape
character itself) in the operand, so that the text is matched literally: `name contains '50%'` must not match
`'500'`. It must also use the binary collation required for [strings](#strings), because most default
collations are case-insensitive and would change the result.

## Error reference

| Situation                                               | Code                   |
| ------------------------------------------------------- | ---------------------- |
| Types cannot be compared, or an operator does not accept the operand types | `TYPE_MISMATCH` |
| `id eq 1 eq 2`, or a misspelled operator such as `EQ`   | `UNEXPECTED_TOKEN`     |
| A number that does not fit `decimal`                    | `NUMBER_OUT_OF_RANGE`  |
