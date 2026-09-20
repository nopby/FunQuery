namespace FunQuery;

public enum TokenType
{
    Identifier,
    StringLiteral,
    OpenRoundParenthesis,
    CloseRoundParenthesis,
    ComparisonOperator,
    LogicalOperator,
    Call,
    Dot,
    OpenSquareParenthesis,
    CloseSquareParenthesis,
    OpenCurlyParenthesis,
    CloseCurlyParenthesis,
    Comma,
    Colon,
    Number
}