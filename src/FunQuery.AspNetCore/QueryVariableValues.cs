using System.Globalization;
using System.Text.Json;
using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.AspNetCore;

/// <summary>
/// Aturan tunggal untuk nilai variabel, dipakai GET (<c>v.name</c>) dan POST
/// (<c>variables</c>): JSON scalar dipetakan ke tipe bahasa, teks lain menjadi string.
/// </summary>
internal static class QueryVariableValues
{
    /// <summary>Nama variable yang dipakai adapter untuk source terpilih.</summary>
    internal const string SourceVariableName = "source";

    public static void ValidateName(string name)
    {
        if (name.Length == 0)
            throw InvalidParameter("A query variable must have a non-empty name.");

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

    /// <summary>GET: parse teks setelah URL decode.</summary>
    public static QueryParameter FromText(string name, string raw)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            // Bukan JSON: seluruh teks adalah string.
            return new QueryParameter(SemanticTypeOptions.String, raw);
        }

        using (document)
            return FromJsonElement(name, document.RootElement);
    }

    /// <summary>POST: nilai sudah berupa elemen JSON dari body.</summary>
    public static QueryParameter FromJsonElement(string name, JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String =>
                new QueryParameter(SemanticTypeOptions.String, element.GetString()),

            JsonValueKind.True =>
                new QueryParameter(SemanticTypeOptions.Boolean, true),

            JsonValueKind.False =>
                new QueryParameter(SemanticTypeOptions.Boolean, false),

            JsonValueKind.Null =>
                new QueryParameter(SemanticTypeOptions.Null, null),

            JsonValueKind.Number =>
                ParseNumber(name, element.GetRawText()),

            _ => throw InvalidParameter(
                $"Query variable '{name}' must be a JSON scalar " +
                "(string, number, bool, or null)."),
        };

    /// <summary>
    /// Panjang nilai untuk batas <see cref="QueryLimits.MaxParameterValueLength"/>: panjang teks
    /// untuk string, panjang representasi JSON untuk scalar lain.
    /// </summary>
    public static int ValueLength(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Length ?? 0
            : element.GetRawText().Length;

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

    internal static QueryException InvalidParameter(string message) =>
        new(QueryErrorCode.InvalidParameter, message);
}
