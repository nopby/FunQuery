namespace FunQuery.Enums;

/// <summary>
/// Aturan apakah sebuah function boleh dipanggil pada target, mis. x.$fn(...).
/// </summary>
public enum TargetRule
{
    /// <summary>Boleh dengan atau tanpa target (default).</summary>
    Optional,

    /// <summary>Wajib dipanggil pada target, mis. <c>$source(...).$filter(...)</c>.</summary>
    Required,

    /// <summary>Tidak boleh punya target: hanya boleh menjadi awal chain, mis. <c>$source(...)</c>.</summary>
    Forbidden,
}
