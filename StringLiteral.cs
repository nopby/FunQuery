namespace FunQuery;

internal static class StringLiteral
{
    /// <summary>
    /// Isi sebuah string literal: buang tanda kutip pembuka dan penutup,
    /// lalu ubah setiap '' di dalamnya menjadi satu tanda kutip.
    /// </summary>
    public static string Unquote(ReadOnlySpan<char> tokenText) =>
        tokenText[1..^1].ToString().Replace("''", "'");
}
