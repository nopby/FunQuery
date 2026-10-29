namespace FunQuery.Server.Playground;

/// <summary>
/// Konfigurasi playground milik host contoh. Playground adalah alat developer, jadi host produksi
/// harus mematikan <see cref="Enabled"/> atau setidaknya <see cref="ExposeSourceNames"/>.
/// </summary>
public sealed class PlaygroundOptions
{
    public bool Enabled { get; init; }

    /// <summary>Bila false, daftar source dan contoh yang memakai source tidak dikirim ke browser.</summary>
    public bool ExposeSourceNames { get; init; }

    /// <summary>Nama source yang boleh ditampilkan di datalist playground.</summary>
    public string[] SourceNames { get; init; } = [];

    public PlaygroundExample[] Examples { get; init; } = [];
}

/// <summary>Satu contoh query di playground. <see cref="VariablesJson"/> adalah object JSON mentah.</summary>
public sealed record PlaygroundExample(
    string Name,
    string Query,
    string? Source,
    string? VariablesJson);

/// <summary>Payload <c>GET /playground/config</c>.</summary>
public sealed record PlaygroundConfig(
    bool ExposeSourceNames,
    string[] Sources,
    PlaygroundExample[] Examples);
