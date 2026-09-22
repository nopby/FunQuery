using FunQuery.Enums;

namespace FunQuery.Extensions;

public static class QueryErrorCodeExtensions
{
    // Sengaja switch eksplisit, bukan Enum.ToString():
    // string ini menjadi kontrak publik, jadi tidak boleh ikut berubah bila enum di-rename.
    // Juga tidak memakai reflection, sehingga aman untuk Native AOT.
    public static string ToCode(this QueryErrorCode code) => code switch
    {
        QueryErrorCode.UnexpectedCharacter => "UNEXPECTED_CHARACTER",
        QueryErrorCode.UnterminatedString => "UNTERMINATED_STRING",
        QueryErrorCode.InvalidNumber => "INVALID_NUMBER",

        QueryErrorCode.UnexpectedToken => "UNEXPECTED_TOKEN",
        QueryErrorCode.UnexpectedEndOfInput => "UNEXPECTED_END_OF_INPUT",
        QueryErrorCode.UnsupportedOperator => "UNSUPPORTED_OPERATOR",
        QueryErrorCode.ExpressionOutsideFunction => "EXPRESSION_OUTSIDE_FUNCTION",

        QueryErrorCode.UnknownFunction => "UNKNOWN_FUNCTION",
        QueryErrorCode.UnknownIdentifier => "UNKNOWN_IDENTIFIER",
        QueryErrorCode.TypeMismatch => "TYPE_MISMATCH",
        QueryErrorCode.InvalidArgumentCount => "INVALID_ARGUMENT_COUNT",
        QueryErrorCode.InvalidTarget => "INVALID_TARGET",
        QueryErrorCode.IncompatibleElementTypes => "INCOMPATIBLE_ELEMENT_TYPES",
        QueryErrorCode.DuplicateField => "DUPLICATE_FIELD",
        QueryErrorCode.NumberOutOfRange => "NUMBER_OUT_OF_RANGE",
        QueryErrorCode.NotSupported => "NOT_SUPPORTED",

        QueryErrorCode.UndefinedVariable => "UNDEFINED_VARIABLE",
        QueryErrorCode.VariableRedefined => "VARIABLE_REDEFINED",
        QueryErrorCode.InvalidLetTarget => "INVALID_LET_TARGET",
        QueryErrorCode.ItemOutOfContext => "ITEM_OUT_OF_CONTEXT",

        QueryErrorCode.InputTooLong => "INPUT_TOO_LONG",
        QueryErrorCode.MaxDepthExceeded => "MAX_DEPTH_EXCEEDED",
        QueryErrorCode.TooManyTokens => "TOO_MANY_TOKENS",

        _ => "INTERNAL_ERROR",
    };

    /// <summary>
    /// True bila error berasal dari bug di FunQuery (mis. node tanpa tipe setelah analisis).
    /// Di lapisan HTTP dipetakan ke 500 dan pesannya tidak perlu ditampilkan mentah ke klien.
    /// </summary>
    public static bool IsInternal(this QueryErrorCode code) =>
        code == QueryErrorCode.InternalError;
}
