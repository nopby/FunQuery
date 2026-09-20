namespace FunQuery;

/// <summary>
/// Rentang karakter pada ekspresi sumber: Start inklusif, End eksklusif (0-based).
/// </summary>
public readonly record struct SourceSpan(int Start, int End)
{
    public int Length => End - Start;

    public static SourceSpan From(Token token) =>
        new(token.StartPosition, token.EndPosition);

    public static SourceSpan Between(SourceSpan first, SourceSpan last) =>
        new(first.Start, last.End);
}
