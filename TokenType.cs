namespace Console;

public enum TokenType
{
    Identifier,
    StringLiteral,
    OpenRoundParenthesis,
    CloseRoundParenthesis,
    ComparisonOperator,
    LogicalOperator,
    Function,
    Dot,
    OpenSquareParenthesis,
    CloseSquareParenthesis,
    OpenCurlyParenthesis,
    CloseCurlyParenthesis,
    Comma,
    Colon,
    IntegerLiteral,
    UIntLiteral,
    LongLiteral,
    ULongLiteral,

    FloatLiteral,
    DoubleLiteral,
    DecimalLiteral,
}