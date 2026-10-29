using FunQuery.Expressions;

namespace FunQuery;

/// <summary>
/// Query yang sudah di-lex, di-parse, dan dianalisis, siap dieksekusi dengan nilai parameter.
/// Dipakai host untuk memisahkan validasi (mis. field sumber yang tidak dikenal) dari pemuatan
/// data: <see cref="QueryEngine.Prepare"/> gagal lebih dulu bila query tidak cocok dengan schema.
/// </summary>
public sealed class QueryPlan
{
    internal QueryPlan(
        string query,
        BaseExpression expression,
        QueryLimits limits)
    {
        Query = query;
        Expression = expression;
        Limits = limits;
    }

    /// <summary>Teks query asli. Posisi token di AST merujuk ke teks ini.</summary>
    public string Query { get; }

    public BaseExpression Expression { get; }

    /// <summary>Batas yang dipakai saat query dianalisis; executor provider memakainya juga.</summary>
    public QueryLimits Limits { get; }
}
