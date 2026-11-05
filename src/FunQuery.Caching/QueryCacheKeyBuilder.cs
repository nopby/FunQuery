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

        Append(canonical, "version", _options.Version);
        Append(canonical, "query", query);
        Append(canonical, "source", sourceName ?? string.Empty);
        Append(canonical, "scope", scope ?? string.Empty);

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
