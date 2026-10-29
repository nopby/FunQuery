namespace FunQuery.Sql.Sql;

public sealed record SqlQuery(
    IReadOnlyList<SqlProjection> Projections,
    SqlSource? Source,
    SqlExpression? Where,
    IReadOnlyList<SqlOrder> OrderBy,
    SqlExpression? Limit,
    SqlExpression? Offset,
    bool Distinct,
    IReadOnlyList<SqlExpression> DistinctOn);

public sealed record SqlProjection(
    SqlExpression Expression,
    string? Alias);

public abstract record SqlSource;

public sealed record SqlTableSource(string Table) : SqlSource;

public sealed record SqlSubquerySource(SqlQuery Query, string Alias) : SqlSource;

public sealed record SqlOrder(
    SqlExpression Expression,
    bool Descending,
    bool NullsFirst);
