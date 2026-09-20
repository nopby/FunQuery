using System.Diagnostics.CodeAnalysis;

namespace FunQuery;

/// <summary>
/// Kumpulan function yang dikenal bahasa. Dikonfigurasi saat startup, lalu dibekukan
/// (<see cref="Freeze"/>) dan dipakai bersama oleh semua request.
/// Tidak thread-safe selama masih dikonfigurasi; setelah dibekukan hanya-baca.
/// </summary>
public sealed class FunctionRegistry
{
    private readonly Dictionary<string, FunctionDefinition> _functions =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _owners =
        new(StringComparer.Ordinal);

    private string _currentOwner = "host";

    public bool IsFrozen { get; private set; }

    public IReadOnlyCollection<FunctionDefinition> Functions => _functions.Values;

    /// <summary>
    /// Mendaftarkan function. Melempar <see cref="InvalidOperationException"/> bila nama sudah dipakai
    /// atau registry sudah dibekukan, dan <see cref="ArgumentException"/> bila definisinya tidak valid.
    /// Semua ini adalah kesalahan konfigurasi dan harus gagal saat startup.
    /// </summary>
    public FunctionRegistry Add(FunctionDefinition function)
    {
        ArgumentNullException.ThrowIfNull(function);
        Validate(function);

        if (IsFrozen)
        {
            throw new InvalidOperationException(
                $"Cannot register '{function.Name}': the registry is frozen. " +
                "Register all functions before the first query.");
        }

        if (_owners.TryGetValue(function.Name, out var owner))
        {
            throw new InvalidOperationException(
                $"Function '{function.Name}' is already registered by '{owner}' " +
                $"and cannot be registered again by '{_currentOwner}'.");
        }

        _functions[function.Name] = function;
        _owners[function.Name] = _currentOwner;

        return this;
    }

    public FunctionRegistry AddExtension(IQueryExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        var previous = _currentOwner;
        _currentOwner = extension.Name;

        try
        {
            extension.Register(this);
        }
        finally
        {
            _currentOwner = previous;
        }

        return this;
    }

    /// <summary>Membekukan registry. Aman dipanggil berulang.</summary>
    public FunctionRegistry Freeze()
    {
        IsFrozen = true;
        return this;
    }

    public bool TryGet(string name, [NotNullWhen(true)] out FunctionDefinition? function) =>
        _functions.TryGetValue(name, out function);

    private static void Validate(FunctionDefinition function)
    {
        if (!IsValidName(function.Name))
        {
            throw new ArgumentException(
                $"Invalid function name '{function.Name}'. " +
                "Names must be '$' followed by lowercase letters, digits, or '_' (not starting with a digit).",
                nameof(function));
        }

        ArgumentNullException.ThrowIfNull(function.Parameters);

        if (function.IsVariadic && function.Parameters.Count == 0)
        {
            throw new ArgumentException(
                $"Function '{function.Name}' is variadic but declares no parameter to type its arguments.",
                nameof(function));
        }

        var sawOptional = false;

        foreach (var parameter in function.Parameters)
        {
            if (!parameter.Required)
                sawOptional = true;
            else if (sawOptional)
                throw new ArgumentException(
                    $"Function '{function.Name}': required parameter '{parameter.Name}' " +
                    "cannot come after an optional parameter.",
                    nameof(function));
        }
    }

    private static bool IsValidName(string name)
    {
        if (name.Length < 2 || name[0] != '$')
            return false;

        if (name[1] is not ((>= 'a' and <= 'z') or '_'))
            return false;

        for (int i = 2; i < name.Length; i++)
        {
            if (name[i] is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                return false;
        }

        return true;
    }
}
