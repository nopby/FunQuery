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
    private int _maxSourceItems = 100_000;
    private int _maxParameters = 32;
    private int _maxParameterValueLength = 1024;
    private TimeSpan _maxExecutionTime = TimeSpan.FromSeconds(2);
    private int _maxResultItems = 10_000;

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

    /// <summary>
    /// Jumlah baris maksimum yang boleh dimuat dari satu source terdaftar. Default 100.000.
    /// Melebihi batas ini adalah <c>SOURCE_TOO_LARGE</c>, bukan pemotongan diam-diam.
    /// </summary>
    public int MaxSourceItems
    {
        get => _maxSourceItems;
        init => _maxSourceItems = AtLeastOne(value, nameof(MaxSourceItems));
    }

    /// <summary>
    /// Jumlah maksimum parameter <c>v.</c> pada satu request. Default 32; 0 menonaktifkan
    /// variabel dari query string. Melebihi batas ini adalah <c>INVALID_PARAMETER</c>.
    /// </summary>
    public int MaxParameters
    {
        get => _maxParameters;
        init
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(MaxParameters),
                    value,
                    $"{nameof(MaxParameters)} must not be negative.");

            _maxParameters = value;
        }
    }

    /// <summary>
    /// Panjang maksimum satu nilai parameter (karakter, setelah URL didecode). Default 1024.
    /// </summary>
    public int MaxParameterValueLength
    {
        get => _maxParameterValueLength;
        init => _maxParameterValueLength = AtLeastOne(value, nameof(MaxParameterValueLength));
    }

    /// <summary>
    /// Batas waktu eksekusi satu query. Default 2 detik. Pemeriksaan bersifat kooperatif: dilakukan
    /// di antara elemen dan di setiap pemanggilan function, bukan dengan menghentikan thread paksa.
    /// </summary>
    public TimeSpan MaxExecutionTime
    {
        get => _maxExecutionTime;
        init
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(
                    nameof(MaxExecutionTime),
                    value,
                    $"{nameof(MaxExecutionTime)} must be positive.");

            _maxExecutionTime = value;
        }
    }

    /// <summary>
    /// Jumlah item maksimum pada hasil array. Default 10.000; 0 berarti hanya array kosong yang
    /// boleh. Melebihi batas ini adalah <c>RESULT_TOO_LARGE</c>, bukan pemotongan diam-diam.
    /// Hasil scalar (mis. <c>$count()</c>) tidak terkena batas ini.
    /// </summary>
    public int MaxResultItems
    {
        get => _maxResultItems;
        init
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(MaxResultItems),
                    value,
                    $"{nameof(MaxResultItems)} must not be negative.");

            _maxResultItems = value;
        }
    }

    private static int AtLeastOne(int value, string name)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be at least 1.");

        return value;
    }
}
