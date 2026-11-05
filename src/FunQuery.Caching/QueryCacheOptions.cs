namespace FunQuery.Caching;

/// <summary>
/// Konfigurasi cache hasil query. Cache bersifat opt-in; host harus memanggil
/// <c>AddFunQueryQueryCache</c> dan mengaktifkan <see cref="Enabled"/>.
/// </summary>
public sealed class QueryCacheOptions
{
    /// <summary>Bila false, cache dilewati sepenuhnya dan query selalu dieksekusi.</summary>
    public bool Enabled { get; set; }

    /// <summary>Umur entry di L1 (memory). Harus lebih pendek dari <see cref="DistributedTtl"/>.</summary>
    public TimeSpan LocalTtl { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Umur entry di L2 (Valkey/Redis).</summary>
    public TimeSpan DistributedTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Ukuran maksimum payload yang boleh masuk cache. Payload lebih besar tidak di-cache.</summary>
    public int MaxEntryBytes { get; set; } = 256 * 1024;

    /// <summary>Jumlah maksimum entry di L1.</summary>
    public int MaxEntries { get; set; } = 1024;

    /// <summary>Prefix key di L1/L2. Ubah saat format entry berubah.</summary>
    public string KeyPrefix { get; set; } = "fq:q:v1:";

    /// <summary>Versi skema/aplikasi yang ikut masuk cache key.</summary>
    public string Version { get; set; } = "1";

    internal void Validate()
    {
        if (LocalTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(LocalTtl),
                LocalTtl,
                $"{nameof(LocalTtl)} must be positive.");

        if (DistributedTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(DistributedTtl),
                DistributedTtl,
                $"{nameof(DistributedTtl)} must be positive.");

        if (LocalTtl > DistributedTtl)
            throw new ArgumentOutOfRangeException(
                nameof(LocalTtl),
                LocalTtl,
                $"{nameof(LocalTtl)} must not exceed {nameof(DistributedTtl)}.");

        if (MaxEntryBytes < 1)
            throw new ArgumentOutOfRangeException(
                nameof(MaxEntryBytes),
                MaxEntryBytes,
                $"{nameof(MaxEntryBytes)} must be at least 1.");

        if (MaxEntries < 1)
            throw new ArgumentOutOfRangeException(
                nameof(MaxEntries),
                MaxEntries,
                $"{nameof(MaxEntries)} must be at least 1.");

        if (string.IsNullOrWhiteSpace(KeyPrefix))
            throw new ArgumentException(
                $"{nameof(KeyPrefix)} must not be empty.",
                nameof(KeyPrefix));

        if (string.IsNullOrWhiteSpace(Version))
            throw new ArgumentException(
                $"{nameof(Version)} must not be empty.",
                nameof(Version));
    }
}
