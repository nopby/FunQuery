using System.Diagnostics.CodeAnalysis;

namespace FunQuery.AspNetCore;

/// <summary>
/// Daftar source yang boleh diakses query. Dikonfigurasi saat startup lalu dibekukan, sama seperti
/// <see cref="FunctionRegistry"/>. Tidak ada operasi enumerasi, sehingga klien tidak bisa
/// menemukan nama source yang tidak disebutkan sendiri.
/// </summary>
public sealed class SourceRegistry
{
    public const int MaxNameLength = 64;

    private readonly Dictionary<string, IQuerySource> _sources =
        new(StringComparer.Ordinal);

    /// <summary>Registry kosong yang sudah dibekukan, dipakai bila host tidak mendaftarkan source.</summary>
    public static SourceRegistry Empty { get; } = new SourceRegistry().Freeze();

    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Mendaftarkan source. Nama duplikat, nama tidak valid, dan penambahan setelah freeze adalah
    /// kesalahan konfigurasi dan gagal saat startup.
    /// </summary>
    public SourceRegistry Add(IQuerySource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (IsFrozen)
            throw new InvalidOperationException(
                $"Cannot register source '{source.Name}': the registry is frozen.");

        ValidateName(source.Name);

        if (!_sources.TryAdd(source.Name, source))
            throw new InvalidOperationException(
                $"Source '{source.Name}' is already registered.");

        return this;
    }

    public SourceRegistry Freeze()
    {
        IsFrozen = true;
        return this;
    }

    public bool TryGet(string name, [NotNullWhen(true)] out IQuerySource? source) =>
        _sources.TryGetValue(name, out source);

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
            throw new ArgumentException(
                $"Invalid source name '{name}'. It must be 1-{MaxNameLength} characters.",
                nameof(name));

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.'))
            {
                throw new ArgumentException(
                    $"Invalid source name '{name}'. Use ASCII letters, digits, '_', '-', or '.'.",
                    nameof(name));
            }
        }
    }
}
