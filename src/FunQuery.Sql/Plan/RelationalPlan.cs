using FunQuery.SemanticTypes;

namespace FunQuery.Sql.Plan;

/// <summary>
/// Rencana relasional provider-neutral. Provider SQL/LINQ menerjemahkan node-node ini, bukan AST.
/// </summary>
public abstract record RelationalPlan;

/// <summary>Source terdaftar; nama dan schema datang dari host.</summary>
public sealed record Scan(string SourceName, ObjectType Schema) : RelationalPlan;

/// <summary>Array inline dari query; SQL provider boleh menolaknya lewat capability.</summary>
public sealed record Values(
    IReadOnlyList<PlanExpression> Rows,
    SemanticType RowType) : RelationalPlan;

public sealed record Filter(
    RelationalPlan Input,
    PlanExpression Predicate) : RelationalPlan;

/// <summary>Satu kolom hasil. Key null berarti proyeksi scalar (mis. $map).</summary>
public sealed record Projection(string? Key, PlanExpression Expression);

public sealed record Project(
    RelationalPlan Input,
    IReadOnlyList<Projection> Projections,
    bool ProducesObject) : RelationalPlan;

public sealed record Sort(
    RelationalPlan Input,
    PlanExpression Key,
    bool Descending) : RelationalPlan;

public sealed record Take(
    RelationalPlan Input,
    PlanExpression Count) : RelationalPlan;

public sealed record Skip(
    RelationalPlan Input,
    PlanExpression Count) : RelationalPlan;

public sealed record Distinct(
    RelationalPlan Input,
    PlanExpression? Key) : RelationalPlan;

public enum AggregateFunction
{
    Count,
}

public sealed record Aggregate(
    RelationalPlan Input,
    AggregateFunction Function) : RelationalPlan;

public sealed record Any(
    RelationalPlan Input,
    PlanExpression? Predicate) : RelationalPlan;

public sealed record First(
    RelationalPlan Input,
    PlanExpression? Predicate) : RelationalPlan;
