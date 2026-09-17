namespace Console;

public readonly record struct Token(TokenType Type, int StartPosition, int EndPosition);