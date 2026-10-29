using System.Diagnostics;
using FunQuery.Enums;

namespace FunQuery.Execution;

/// <summary>
/// Pemeriksaan kooperatif untuk satu eksekusi: batas waktu dan pembatalan. Dipanggil di antara
/// elemen dan di setiap pemanggilan function. Timeout menjadi <c>EXECUTION_TIMEOUT</c>;
/// pembatalan diteruskan sebagai <see cref="OperationCanceledException"/>.
/// </summary>
internal sealed class ExecutionGuard
{
    private readonly long _start;
    private readonly TimeSpan _timeout;
    private readonly CancellationToken _cancellationToken;

    public ExecutionGuard(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _start = Stopwatch.GetTimestamp();
        _timeout = timeout;
        _cancellationToken = cancellationToken;
    }

    public void Step()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        if (Stopwatch.GetElapsedTime(_start) >= _timeout)
        {
            throw new QueryException(
                QueryErrorCode.ExecutionTimeout,
                "The query exceeded its time limit.");
        }
    }
}
