namespace FunQuery.Enums;

/// <summary>
/// Kode error baku. Nilai enum hanya untuk kode C#; yang dikirim ke klien
/// adalah string dari <see cref="QueryErrorCodeExtensions.ToCode"/> (stabil, tidak boleh diubah).
/// </summary>
public enum QueryErrorCode
{
    // Lexer
    UnexpectedCharacter,
    UnterminatedString,
    InvalidNumber,

    // Parser
    UnexpectedToken,
    UnexpectedEndOfInput,
    UnsupportedOperator,

    // Semantic analyzer
    UnknownFunction,
    UnknownIdentifier,
    TypeMismatch,
    InvalidArgumentCount,
    InvalidTarget,
    IncompatibleElementTypes,
    DuplicateField,
    NumberOutOfRange,

    // Batas keamanan (dipakai saat batas-batas itu diimplementasikan)
    InputTooLong,
    MaxDepthExceeded,
    TooManyTokens,

    // Bug di FunQuery sendiri, bukan kesalahan input pengguna
    InternalError,
}
