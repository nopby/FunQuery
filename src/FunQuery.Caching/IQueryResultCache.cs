namespace FunQuery.Caching;

/// <summary>
/// Cache hasil query bertingkat. Implementasi default memakai L1 memory dan L2 opsional,
/// dengan single-flight per key.
/// </summary>
public interface IQueryResultCache
{
    /// <summary>False bila cache dimatikan; pemanggil harus mengeksekusi query langsung.</summary>
    bool Enabled { get; }

    ValueTask<byte[]?> TryGetAsync(string key, CancellationToken cancellationToken);

    ValueTask SetAsync(
        string key,
        byte[] payload,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mengembalikan payload dari cache, atau menjalankan <paramref name="factory"/> satu kali
    /// per key untuk request yang bersamaan. Cancellation caller tidak membatalkan waiter lain.
    /// </summary>
    ValueTask<byte[]> GetOrCreateAsync(
        string key,
        Func<CancellationToken, ValueTask<byte[]>> factory,
        CancellationToken cancellationToken);
}
