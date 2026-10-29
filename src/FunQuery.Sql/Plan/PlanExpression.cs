using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Sql.Plan;

/// <summary>
/// Ekspresi provider-neutral. Tidak ada dialect, SQL, atau tipe database di sini.
/// </summary>
public abstract record PlanExpression(SemanticType Type);

/// <summary>Field dari baris saat ini. Path bertitik untuk field bersarang, mis. "address.city".</summary>
public sealed record Column(string Path, SemanticType Type) : PlanExpression(Type);

/// <summary>Baris/nilai saat ini (<c>~</c>).</summary>
public sealed record CurrentRow(SemanticType Type) : PlanExpression(Type);

public sealed record Literal(object? Value, SemanticType Type) : PlanExpression(Type);

/// <summary>Parameter request, dirujuk dengan nama tanpa '@'.</summary>
public sealed record Parameter(string Name, SemanticType Type) : PlanExpression(Type);

public sealed record Binary(
    ComparisonOperator Operator,
    PlanExpression Left,
    PlanExpression Right,
    SemanticType Type) : PlanExpression(Type);

public sealed record Logical(
    LogicalOperator Operator,
    PlanExpression Left,
    PlanExpression Right,
    SemanticType Type) : PlanExpression(Type);

public sealed record Not(PlanExpression Operand, SemanticType Type) : PlanExpression(Type);

/// <summary>Pemanggilan scalar function: $lower, $upper, $length, $coalesce, $date.</summary>
public sealed record Call(
    string Name,
    IReadOnlyList<PlanExpression> Arguments,
    SemanticType Type) : PlanExpression(Type);

public sealed record ObjectField(string Name, PlanExpression Value);

public sealed record ObjectConstruct(
    IReadOnlyList<ObjectField> Fields,
    SemanticType Type) : PlanExpression(Type);

public sealed record ArrayConstruct(
    IReadOnlyList<PlanExpression> Elements,
    SemanticType Type) : PlanExpression(Type);

/// <summary>Posisi elemen ($index), dengan base opsional.</summary>
public sealed record IndexExpression(PlanExpression? Base, SemanticType Type) : PlanExpression(Type);

/// <summary>Akses field pada nilai yang bukan baris saat ini, mis. hasil subquery.</summary>
public sealed record Field(
    PlanExpression Target,
    string Name,
    SemanticType Type) : PlanExpression(Type);

/// <summary>Chain bersarang, mis. $map($source([...]).$count()).</summary>
public sealed record Subquery(RelationalPlan Plan, SemanticType Type) : PlanExpression(Type);
