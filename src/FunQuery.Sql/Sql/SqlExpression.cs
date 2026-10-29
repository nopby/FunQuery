namespace FunQuery.Sql.Sql;

/// <summary>Ekspresi SQL. Nilai pengguna tidak pernah masuk sebagai teks; selalu parameter.</summary>
public abstract record SqlExpression;

public sealed record SqlColumn(string Path) : SqlExpression;

public sealed record SqlParameterRef(string Name) : SqlExpression;

/// <summary>Hanya untuk potongan aman seperti NULL/TRUE/FALSE/1, bukan nilai pengguna.</summary>
public sealed record SqlLiteral(string Text) : SqlExpression;

public sealed record SqlBinary(
    string Operator,
    SqlExpression Left,
    SqlExpression Right) : SqlExpression;

public sealed record SqlUnary(
    string Operator,
    SqlExpression Operand) : SqlExpression;

public sealed record SqlFunction(
    string Name,
    IReadOnlyList<SqlExpression> Arguments) : SqlExpression;

public sealed record SqlCast(
    SqlExpression Expression,
    string TypeName) : SqlExpression;

public sealed record SqlSubquery(SqlQuery Query) : SqlExpression;

public sealed record SqlExists(SqlQuery Query) : SqlExpression;

/// <summary>Potongan yang dihasilkan dialect (mis. IS DISTINCT FROM, strpos), bukan input pengguna.</summary>
public sealed record SqlRaw(string Text) : SqlExpression;

public sealed record SqlStar : SqlExpression
{
    public static SqlStar Instance { get; } = new();
}
