using System.Security.Cryptography;
using System.Text;
using FunQuery.Execution;

namespace FunQuery.Caching;

/// <summary>Membuat key cache dari request yang sudah diparse.</summary>
public interface IQueryCacheKeyBuilder
{
    /// <summary>
    /// Membuat key stabil. Nilai <c>@source</c> tidak ikut di-hash: source diwakili oleh
    /// <paramref name="sourceName"/>, bukan oleh baris data yang di-bind ke variabel tersebut.
    /// </summary>
    string Create(
        string query,
        string? sourceName,
        QueryEnvironment variables,
        string? scope);

    /// <summary>
    /// Key untuk named query. Catalog version dan api/tenant ikut di-hash agar edit catalog
    /// tidak memakai entry lama.
    /// </summary>
    string CreateNamed(
        string appId,
        string tenantId,
        string key,
        int version,
        string query,
        string? sourceName,
        QueryEnvironment variables);
}

/// <summary>Key builder default: canonical text + SHA-256, tanpa reflection.</summary>
public sealed class QueryCacheKeyBuilder : IQueryCacheKeyBuilder
{
    private readonly QueryCacheOptions _options;

    public QueryCacheKeyBuilder(QueryCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public string Create(
        string query,
        string? sourceName,
        QueryEnvironment variables,
        string? scope)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentNullException.ThrowIfNull(variables);

        var canonical = new StringBuilder();

        Append(canonical, "kind", "adhoc");
        Append(canonical, "version", _options.Version);
        Append(canonical, "query", query);
        Append(canonical, "source", sourceName ?? string.Empty);
        Append(canonical, "scope", scope ?? string.Empty);
        AppendVariables(canonical, variables);

        return Finalize(canonical);
    }

    public string CreateNamed(
        string appId,
        string tenantId,
        string key,
        int version,
        string query,
        string? sourceName,
        QueryEnvironment variables)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(variables);

        var canonical = new StringBuilder();

        Append(canonical, "kind", "named");
        Append(canonical, "version", _options.Version);
        Append(canonical, "app", appId);
        Append(canonical, "tenant", tenantId);
        Append(canonical, "key", key);
        Append(canonical, "catalog_version", version.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        Append(canonical, "query", query);
        Append(canonical, "source", sourceName ?? string.Empty);
        AppendVariables(canonical, variables);

        return Finalize(canonical);
    }

    private void AppendVariables(
        StringBuilder canonical,
        QueryEnvironment variables)
    {
        foreach (var pair in variables.Parameters.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            // The bound source is host data, not part of the semantic request.
            if (string.Equals(
                    pair.Key,
                    "source",
                    StringComparison.Ordinal))
            {
                continue;
            }

            Append(canonical, "variable", pair.Key);
            Append(canonical, "type", pair.Value.Type.Name);
            Append(canonical, "value", ValueJson.Serialize(pair.Value.Value));
        }
    }

    private string Finalize(StringBuilder canonical)
    {
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical.ToString()));

        return _options.KeyPrefix +
            Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string label, string value)
    {
        builder.Append(label);
        builder.Append(':');
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }
}
