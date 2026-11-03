namespace FunQuery.Sql.Sql;

public sealed record SqlQuery(
    IReadOnlyList<SqlProjection> Projections,
    SqlSource? Source,
    SqlExpression? Where,
    IReadOnlyList<SqlOrder> OrderBy,
    SqlExpression? Limit,
    SqlExpression? Offset,
    bool Distinct,
    IReadOnlyList<SqlExpression> DistinctOn)
{
    /// <summary>CTE yang ditulis sebelum SELECT. Kosong untuk query biasa.</summary>
    public IReadOnlyList<SqlCte> Ctes { get; init; } = [];
}

public sealed record SqlCte(string Name, SqlQuery Query);

public sealed record SqlProjection(
    SqlExpression Expression,
    string? Alias);

public abstract record SqlSource;

public sealed record SqlTableSource(string Table) : SqlSource;

/// <summary>CTE yang dideklarasikan dengan WITH di query yang sama.</summary>
public sealed record SqlCteSource(string Name) : SqlSource;

public sealed record SqlSubquerySource(SqlQuery Query, string Alias) : SqlSource;

public sealed record SqlOrder(
    SqlExpression Expression,
    bool Descending,
    bool NullsFirst);
