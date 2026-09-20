namespace FunQuery;

public readonly record struct Token(TokenType Type, int StartPosition, int EndPosition);