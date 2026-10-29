using System.Text;
using FunQuery.Enums;
using FunQuery.Sql.Sql;

namespace FunQuery.Sql;

/// <summary>Menulis SQL AST menjadi teks, memakai dialect untuk bagian yang berbeda.</summary>
public sealed class SqlWriter
{
    private readonly ISqlDialect _dialect;

    public SqlWriter(ISqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        _dialect = dialect;
    }

    public string Write(SqlQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var builder = new StringBuilder();

        builder.Append("SELECT ");

        if (query.DistinctOn.Count > 0)
        {
            builder.Append("DISTINCT ON (");
            builder.Append(string.Join(", ", query.DistinctOn.Select(WriteExpression)));
            builder.Append(") ");
        }
        else if (query.Distinct)
        {
            builder.Append("DISTINCT ");
        }

        builder.Append(string.Join(", ", query.Projections.Select(WriteProjection)));

        if (query.Source is not null)
        {
            builder.Append(" FROM ");
            builder.Append(WriteSource(query.Source));
        }

        if (query.Where is not null)
        {
            builder.Append(" WHERE ");
            builder.Append(WriteExpression(query.Where));
        }

        if (query.OrderBy.Count > 0)
        {
            builder.Append(" ORDER BY ");
            builder.Append(string.Join(", ", query.OrderBy.Select(WriteOrder)));
        }

        var limit = query.Limit is null ? null : WriteExpression(query.Limit);
        var offset = query.Offset is null ? null : WriteExpression(query.Offset);
        var suffix = _dialect.RenderLimitOffset(limit, offset);

        if (!string.IsNullOrEmpty(suffix))
        {
            builder.Append(' ');
            builder.Append(suffix);
        }

        return builder.ToString();
    }

    public string WriteExpression(SqlExpression expression) =>
        expression switch
        {
            SqlColumn column => QuotePath(column.Path),
            SqlParameterRef parameter => parameter.Name,
            SqlLiteral literal => literal.Text,
            SqlBinary binary =>
                $"({WriteExpression(binary.Left)} {binary.Operator} " +
                $"{WriteExpression(binary.Right)})",
            SqlUnary unary =>
                $"({unary.Operator} {WriteExpression(unary.Operand)})",
            SqlFunction function =>
                $"{function.Name}({string.Join(", ", function.Arguments.Select(WriteExpression))})",
            SqlCast cast =>
                $"CAST({WriteExpression(cast.Expression)} AS {cast.TypeName})",
            SqlSubquery subquery => $"({Write(subquery.Query)})",
            SqlExists exists => $"EXISTS ({Write(exists.Query)})",
            SqlStar => "*",
            SqlRaw raw => raw.Text,
            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                "Unknown SQL expression."),
        };

    private string WriteProjection(SqlProjection projection)
    {
        var expression = WriteExpression(projection.Expression);

        return projection.Alias is null
            ? expression
            : $"{expression} AS {_dialect.QuoteIdentifier(projection.Alias)}";
    }

    private string WriteSource(SqlSource source) =>
        source switch
        {
            SqlTableSource table => QuotePath(table.Table),
            SqlSubquerySource subquery =>
                $"({Write(subquery.Query)}) AS {_dialect.QuoteIdentifier(subquery.Alias)}",
            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                "Unknown SQL source."),
        };

    public string WriteOrder(SqlOrder order)
    {
        var sql = WriteExpression(order.Expression);
        sql += order.Descending ? " DESC" : " ASC";

        if (_dialect.SupportsNullsOrdering)
            sql += " " + _dialect.RenderNullsOrdering(order.NullsFirst);

        return sql;
    }

    private string QuotePath(string path) =>
        string.Join(".", path.Split('.').Select(_dialect.QuoteIdentifier));
}
