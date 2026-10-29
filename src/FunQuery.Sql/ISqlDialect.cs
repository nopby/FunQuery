using FunQuery.SemanticTypes;

namespace FunQuery.Sql;

/// <summary>
/// Perbedaan yang benar-benar milik database: quoting, parameter, tipe, pagination, collation,
/// null ordering, dan operator kesamaan null-safe.
/// </summary>
public interface ISqlDialect
{
    ProviderCapabilities Capabilities { get; }

    string QuoteIdentifier(string identifier);

    string ParameterName(int index);

    string TypeName(SemanticType type);

    string ApplyStringCollation(string sql);

    string RenderEquality(string left, string right, bool negated);

    string RenderContains(string text, string part);

    string RenderStartsWith(string text, string prefix);

    string RenderEndsWith(string text, string suffix);

    string RenderLimitOffset(string? limit, string? offset);

    bool SupportsNullsOrdering { get; }

    string RenderNullsOrdering(bool nullsFirst);

    string RenderDateCast(string expression);
}
