using FunQuery.SemanticTypes;

namespace FunQuery.AspNetCore;

/// <summary>
/// Satu data source yang didaftarkan host. Hanya source yang terdaftar yang bisa dicapai query
/// lewat <c>$source(@source)</c>; tidak ada akses ke objek aplikasi sembarang.
/// </summary>
public interface IQuerySource
{
    /// <summary>Nama yang dipakai di query string, mis. <c>?source=orders</c>.</summary>
    string Name { get; }

    /// <summary>Field dan tipe yang dilihat analyzer sebelum data dimuat.</summary>
    ObjectType Schema { get; }

    /// <summary>Operasi yang didukung source ini (disimpan untuk provider Milestone 6).</summary>
    SourceCapabilities Capabilities { get; }

    /// <summary>
    /// Memuat seluruh baris. Setiap baris adalah map nama field → nilai runtime; field yang tidak
    /// ada dibaca sebagai null. Nilai harus cocok dengan <see cref="Schema"/>.
    /// </summary>
    ValueTask<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LoadAsync(
        CancellationToken cancellationToken);
}
