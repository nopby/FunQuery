using FunQuery.Enums;
using FunQuery.Extensions;

namespace FunQuery;

/// <summary>
/// Satu-satunya jenis error untuk lexer, parser, dan analyzer.
/// Membawa kode baku, pesan, dan posisi karakter di ekspresi.
/// </summary>
public sealed class QueryException : Exception
{
    /// <param name="code">Kode error baku.</param>
    /// <param name="message">Pesan untuk manusia. Posisi tidak perlu ditulis di sini, sudah ada di <see cref="Position"/>.</param>
    /// <param name="position">Indeks karakter (0-based) pada ekspresi yang sudah didecode. -1 bila tidak diketahui.</param>
    /// <param name="length">Panjang bagian ekspresi yang bermasalah, untuk penyorotan. 0 bila tidak diketahui.</param>
    public QueryException(
        QueryErrorCode code,
        string message,
        int position = -1,
        int length = 0,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Position = position < 0 ? -1 : position;
        Length = position < 0 ? 0 : Math.Max(length, 0);
    }

    /// <summary>Posisi dan panjang diambil dari token.</summary>
    public QueryException(QueryErrorCode code, string message, Token token)
        : this(
            code,
            message,
            token.StartPosition,
            token.EndPosition - token.StartPosition)
    {
    }

    public QueryErrorCode Code { get; }

    /// <summary>Kode string stabil yang dikirim ke klien, mis. "UNKNOWN_FUNCTION".</summary>
    public string CodeName => Code.ToCode();

    /// <summary>Indeks karakter (0-based), atau -1 bila tidak diketahui.</summary>
    public int Position { get; }

    public int Length { get; }

    public bool HasPosition => Position >= 0;

    /// <summary>True bila ini bug di FunQuery, bukan kesalahan input.</summary>
    public bool IsInternal => Code.IsInternal();

    /// <summary>Bentuk ringkas untuk log dan konsol.</summary>
    public string ToDisplayString() =>
        HasPosition
            ? $"[{CodeName}] {Message} (at position {Position})"
            : $"[{CodeName}] {Message}";
}