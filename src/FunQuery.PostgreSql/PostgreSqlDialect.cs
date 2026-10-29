using System.Globalization;
using FunQuery.SemanticTypes;
using FunQuery.Sql;

namespace FunQuery.PostgreSql;

/// <summary>
/// Dialect PostgreSQL. Hanya berisi perbedaan database; plan, null semantics, dan parameterisasi
/// tetap di <c>FunQuery.Sql</c>.
/// </summary>
public sealed class PostgreSqlDialect : ISqlDialect
{
    public static PostgreSqlDialect Instance { get; } = new();

    public ProviderCapabilities Capabilities =>
        ProviderCapabilities.Filtering |
        ProviderCapabilities.Sorting |
        ProviderCapabilities.Projection |
        ProviderCapabilities.Pagination |
        ProviderCapabilities.StringOperations |
        ProviderCapabilities.DateOperations |
        ProviderCapabilities.Aggregation |
        ProviderCapabilities.Distinct |
        ProviderCapabilities.DistinctOn |
        ProviderCapabilities.WindowFunctions;

    public string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public string ParameterName(int index) =>
        "@p" + index.ToString(CultureInfo.InvariantCulture);

    public string TypeName(SemanticType type) =>
        type switch
        {
            IntType => "integer",
            LongType => "bigint",
            DecimalType => "numeric",
            StringType => "text",
            BooleanType => "boolean",
            DateType => "date",
            FloatType => "real",
            DoubleType => "double precision",
            ArrayType or ObjectType => "jsonb",
            _ => "text",
        };

    public string ApplyStringCollation(string sql) =>
        $"({sql}) COLLATE \"C\"";

    /// <summary>IS [NOT] DISTINCT FROM null-safe, sesuai semantik dua-nilai FunQuery.</summary>
    public string RenderEquality(string left, string right, bool negated) =>
        negated
            ? $"(({left}) IS DISTINCT FROM ({right}))"
            : $"(({left}) IS NOT DISTINCT FROM ({right}))";

    public string RenderContains(string text, string part) =>
        $"strpos({text}, {part}) > 0";

    public string RenderStartsWith(string text, string prefix) =>
        $"strpos({text}, {prefix}) = 1";

    public string RenderEndsWith(string text, string suffix) =>
        $"{ApplyStringCollation($"right({text}, length({suffix}))")} = " +
        $"{ApplyStringCollation(suffix)}";

    public string RenderLimitOffset(string? limit, string? offset)
    {
        var parts = new List<string>(2);

        if (limit is not null)
            parts.Add("LIMIT " + limit);

        if (offset is not null)
            parts.Add("OFFSET " + offset);

        return string.Join(" ", parts);
    }

    public bool SupportsNullsOrdering => true;

    public string RenderNullsOrdering(bool nullsFirst) =>
        nullsFirst ? "NULLS FIRST" : "NULLS LAST";

    public string RenderDateCast(string expression) =>
        $"CAST({expression} AS date)";
}
