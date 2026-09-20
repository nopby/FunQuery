using FunQuery.Enums;

namespace FunQuery;

/// <summary>
/// Penghitung kedalaman rekursi parser. Dilewatkan dengan ref supaya tidak ada alokasi.
/// Tidak perlu try/finally: bila terjadi exception, seluruh proses parsing dibatalkan.
/// </summary>
internal struct ParseGuard
{
    private readonly int _maxDepth;
    private int _depth;

    public ParseGuard(int maxDepth)
    {
        _maxDepth = maxDepth;
        _depth = 0;
    }

    public void Enter(int position)
    {
        if (++_depth > _maxDepth)
        {
            throw new QueryException(
                QueryErrorCode.MaxDepthExceeded,
                $"Expression is nested deeper than {_maxDepth} levels.",
                position);
        }
    }

    public void Exit() => _depth--;
}
