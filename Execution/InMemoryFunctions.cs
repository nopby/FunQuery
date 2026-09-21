using System.Diagnostics.CodeAnalysis;
using FunQuery.Expressions;

namespace FunQuery.Execution;

/// <summary>
/// Implementasi in-memory sebuah function. Dipanggil sekali saat query dikompilasi (bukan per baris)
/// dan mengembalikan delegate yang menghitung nilainya. Argumen dikompilasi lewat
/// <see cref="InMemoryCompiler.Compile"/>; argumen per-elemen menerima elemen saat ini.
/// </summary>
public delegate Func<object?, object?> InMemoryFunction(
    InMemoryCompiler compiler,
    CallExpression call);

/// <summary>
/// Tabel implementasi in-memory, dipisahkan dari <see cref="FunctionRegistry"/>: registry mendefinisikan
/// function (nama, tipe, aturan), tabel ini mendefinisikan cara provider in-memory menjalankannya.
/// Function yang terdaftar tetapi tidak punya implementasi ditolak dengan NOT_SUPPORTED, bukan diam-diam.
/// </summary>
public sealed class InMemoryFunctions
{
    private readonly Dictionary<string, InMemoryFunction> _functions =
        new(StringComparer.Ordinal);

    public InMemoryFunctions Add(string name, InMemoryFunction implementation)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(implementation);

        if (!_functions.TryAdd(name, implementation))
        {
            throw new InvalidOperationException(
                $"An in-memory implementation for '{name}' is already registered.");
        }

        return this;
    }

    public bool TryGet(string name, [NotNullWhen(true)] out InMemoryFunction? implementation) =>
        _functions.TryGetValue(name, out implementation);

    /// <summary>Implementasi function inti: $source dan $filter.</summary>
    public static InMemoryFunctions CreateDefault() =>
        new InMemoryFunctions()
            .Add("$source", CoreInMemoryFunctions.Source)
            .Add("$filter", CoreInMemoryFunctions.Filter);
}
