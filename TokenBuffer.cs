using System.Buffers;

namespace FunQuery;

public sealed class TokenBuffer : IDisposable
{
    private Token[] _array;
    public int Count { get; private set; }

    public TokenBuffer(int capacity = 16)
    {
        _array = ArrayPool<Token>.Shared.Rent(capacity);
    }

    public void Add(Token token)
    {
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