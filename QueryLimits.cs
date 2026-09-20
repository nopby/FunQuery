namespace FunQuery;

/// <summary>
/// Batas keamanan untuk satu ekspresi. Semua nilai punya default yang aman,
/// jadi bila tidak dikonfigurasi, <see cref="Default"/> yang dipakai.
/// Objek ini immutable: buat instance baru dengan initializer.
/// </summary>
/// <example>
/// var limits = new QueryLimits { MaxInputLength = 4096, MaxDepth = 32 };
/// </example>
public sealed class QueryLimits
{
    /// <summary>
    /// Batas atas MaxDepth. Parser bersifat rekursif dan stack thread terbatas,
    /// jadi kedalaman di atas ini ditolak agar konfigurasi tidak membuka celah stack overflow.
    /// </summary>
    public const int AbsoluteMaxDepth = 256;

    private int _maxInputLength = 2048;
    private int _maxTokens = 1024;
    private int _maxDepth = 64;

    /// <summary>Instance bersama dengan semua nilai default.</summary>
    public static QueryLimits Default { get; } = new();

    /// <summary>
    /// Panjang maksimum ekspresi (jumlah karakter, setelah URL didecode).
    /// Dicek sebelum lexing. Default 2048.
    /// </summary>
    public int MaxInputLength
    {
        get => _maxInputLength;
        init => _maxInputLength = AtLeastOne(value, nameof(MaxInputLength));
    }

    /// <summary>Jumlah token maksimum. Default 1024.</summary>
    public int MaxTokens
    {
        get => _maxTokens;
        init => _maxTokens = AtLeastOne(value, nameof(MaxTokens));
    }

    /// <summary>
    /// Kedalaman maksimum: kurung/array/object/argumen bersarang di parser, dan
    /// kedalaman struktur ekspresi di analyzer (rantai function dan rantai and/or ikut dihitung).
    /// Default 64, maksimum <see cref="AbsoluteMaxDepth"/>.
    /// </summary>
    public int MaxDepth
    {
        get => _maxDepth;
        init
        {
            if (value < 1 || value > AbsoluteMaxDepth)
                throw new ArgumentOutOfRangeException(
                    nameof(MaxDepth),
                    value,
                    $"{nameof(MaxDepth)} must be between 1 and {AbsoluteMaxDepth}.");

            _maxDepth = value;
        }
    }

    private static int AtLeastOne(int value, string name)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be at least 1.");

        return value;
    }
}
