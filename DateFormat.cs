using System.Globalization;

namespace FunQuery;

/// <summary>
/// Satu-satunya format tanggal bahasa: ISO 8601 tanggal saja (<c>yyyy-MM-dd</c>), invariant.
/// Dipakai analyzer (validasi literal) dan interpreter (parse nilai runtime) supaya keduanya
/// tidak pernah berbeda pendapat. Lihat docs/DataTypes.md#dates.
/// </summary>
public static class DateFormat
{
    public const string Pattern = "yyyy-MM-dd";

    /// <summary>
    /// Parse tanggal yang ditulis tepat sebagai <c>yyyy-MM-dd</c>. Format lain — termasuk
    /// tanggal dengan jam, zona waktu, atau pemisah lain — ditolak.
    /// </summary>
    public static bool TryParse(string? text, out DateOnly value)
    {
        if (text is null || text.Length != Pattern.Length)
        {
            value = default;
            return false;
        }

        return DateOnly.TryParseExact(
            text,
            Pattern,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    public static string Format(DateOnly value) =>
        value.ToString(Pattern, CultureInfo.InvariantCulture);
}
