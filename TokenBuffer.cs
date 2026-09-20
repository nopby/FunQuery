using FunQuery.Enums;
using System.Buffers;

namespace FunQuery;

public sealed class TokenBuffer : IDisposable
{
    private Token[] _array;
    private readonly int _maxTokens;

    public int Count { get; private set; }

    public QueryLimits Limits { get; }

    public TokenBuffer(int capacity = 16, QueryLimits? limits = null)
    {
        Limits = limits ?? QueryLimits.Default;
        _maxTokens = Limits.MaxTokens;
        _array = ArrayPool<Token>.Shared.Rent(Math.Max(1, Math.Min(capacity, _maxTokens)));
    }

    public void Add(Token token)
    {
        if (Count >= _maxTokens)
        {
            throw new QueryException(
                QueryErrorCode.TooManyTokens,
                $"Expression has more than {_maxTokens} tokens.",
                token);
        }

        if (Count == _array.Length)
            Grow();

        _array[Count++] = token;
    }

    public ReadOnlySpan<Token> Span =>
        _array.AsSpan(0, Count);

    private void Grow()
    {
        var newArray =
            ArrayPool<Token>.Shared.Rent(_array.Length * 2);

        _array.AsSpan(0, Count)
              .CopyTo(newArray);

        ArrayPool<Token>.Shared.Return(_array);

        _array = newArray;
    }

    public void Dispose()
    {
        ArrayPool<Token>.Shared.Return(_array);
        _array = [];
    }
}