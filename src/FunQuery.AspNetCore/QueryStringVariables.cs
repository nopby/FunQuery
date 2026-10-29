using System.Globalization;
using System.Text.Json;
using FunQuery.Enums;
using FunQuery.SemanticTypes;
using Microsoft.AspNetCore.Http;

namespace FunQuery.AspNetCore;

/// <summary>
/// Membaca variabel dari query string: setiap parameter <c>v.&lt;nama&gt;</c> menjadi
/// <c>@nama</c>. Nilai diparse sebagai JSON scalar bila bisa; teks lain menjadi string. Tidak ada
/// evaluasi kode, hanya parsing data.
/// </summary>
internal static class QueryStringVariables
{
    internal const string Prefix = "v.";

    /// <summary>Nama variable yang dipakai adapter untuk source terpilih.</summary>
    internal const string SourceVariableName = "source";

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
            ValidateName(name);

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

            parameters[name] = Parse(name, raw);
        }

        return new QueryEnvironment(parameters);
    }

    private static void ValidateName(string name)
    {
        if (name.Length == 0)
            throw InvalidParameter("A query variable must have a name after 'v.'.");

        if (!char.IsAsciiLetter(name[0]) && name[0] != '_')
        {
            throw InvalidParameter(
                $"Query variable name '{name}' must start with an ASCII letter or '_'.");
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                throw InvalidParameter(
                    $"Query variable name '{name}' may contain only ASCII letters, digits, and '_'.");
            }
        }

        if (name == SourceVariableName)
        {
            throw InvalidParameter(
                "Query variable name 'source' is reserved for the selected source.");
        }
    }

    private static QueryParameter Parse(string name, string raw)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            // Bukan JSON: seluruh teks (setelah URL decode) adalah string.
            return new QueryParameter(SemanticTypeOptions.String, raw);
        }

        using (document)
        {
            var root = document.RootElement;

            return root.ValueKind switch
            {
                JsonValueKind.String =>
                    new QueryParameter(SemanticTypeOptions.String, root.GetString()),

                JsonValueKind.True =>
                    new QueryParameter(SemanticTypeOptions.Boolean, true),

                JsonValueKind.False =>
                    new QueryParameter(SemanticTypeOptions.Boolean, false),

                JsonValueKind.Null =>
                    new QueryParameter(SemanticTypeOptions.Null, null),

                JsonValueKind.Number =>
                    ParseNumber(name, root.GetRawText()),

                _ => throw InvalidParameter(
                    $"Query variable '{name}' must be a JSON scalar " +
                    "(string, number, bool, or null)."),
            };
        }
    }

    private static QueryParameter ParseNumber(string name, string raw)
    {
        // Sama dengan aturan literal: bilangan bulat menjadi int/long/decimal, sedangkan
        // bentuk pecahan/eksponen menjadi decimal.
        if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E'))
        {
            if (decimal.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var decimalValue))
            {
                return new QueryParameter(SemanticTypeOptions.Decimal, decimalValue);
            }

            throw OutOfRange(name);
        }

        if (int.TryParse(
                raw,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var intValue))
        {
            return new QueryParameter(SemanticTypeOptions.Int, intValue);
        }

        if (long.TryParse(
                raw,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var longValue))
        {
            return new QueryParameter(SemanticTypeOptions.Long, longValue);
        }

        if (decimal.TryParse(
                raw,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var bigValue))
        {
            return new QueryParameter(SemanticTypeOptions.Decimal, bigValue);
        }

        throw OutOfRange(name);
    }

    private static QueryException OutOfRange(string name) =>
        InvalidParameter($"Query variable '{name}' is a number outside the supported range.");

    private static QueryException InvalidParameter(string message) =>
        new(QueryErrorCode.InvalidParameter, message);
}
