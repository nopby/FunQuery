using FunQuery.Enums;
using Microsoft.AspNetCore.Http;

namespace FunQuery.AspNetCore;

/// <summary>
/// Membaca variabel dari query string: setiap parameter <c>v.&lt;nama&gt;</c> menjadi
/// <c>@nama</c>. Nilai dan nama divalidasi oleh <see cref="QueryVariableValues"/>.
/// </summary>
internal static class QueryStringVariables
{
    internal const string Prefix = "v.";

    public static QueryEnvironment Read(IQueryCollection query, QueryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(limits);

        var parameters = new Dictionary<string, QueryParameter>(StringComparer.Ordinal);

        foreach (var (key, values) in query)
        {
            if (!key.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            var name = key[Prefix.Length..];
            QueryVariableValues.ValidateName(name);

            if (parameters.Count >= limits.MaxParameters)
            {
                throw InvalidParameter(
                    $"Too many query variables; at most {limits.MaxParameters} are allowed.");
            }

            if (values.Count != 1)
            {
                throw InvalidParameter(
                    $"Query variable '{name}' is supplied more than once.");
            }

            var raw = values[0] ?? string.Empty;

            if (raw.Length > limits.MaxParameterValueLength)
            {
                throw InvalidParameter(
                    $"Query variable '{name}' is longer than " +
                    $"{limits.MaxParameterValueLength} characters.");
            }

            parameters[name] = QueryVariableValues.FromText(name, raw);
        }

        return new QueryEnvironment(parameters);
    }

    private static QueryException InvalidParameter(string message) =>
        new(QueryErrorCode.InvalidParameter, message);
}
