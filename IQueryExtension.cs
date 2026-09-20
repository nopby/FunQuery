namespace FunQuery;

/// <summary>
/// Titik ekstensi untuk menambah function ke bahasa (mis. provider SQL menambah $cte).
/// Ekstensi didaftarkan secara eksplisit lewat <see cref="FunctionRegistry.AddExtension"/>;
/// tidak ada pemindaian assembly, supaya aman untuk Native AOT.
/// </summary>
public interface IQueryExtension
{
    /// <summary>Nama ekstensi, dipakai di pesan error pendaftaran (mis. nama ganda).</summary>
    string Name { get; }

    void Register(FunctionRegistry registry);
}
