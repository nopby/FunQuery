using FunQuery.Expressions;

namespace FunQuery.Execution;

/// <summary>
/// Provider in-memory: mengeksekusi query yang sudah dianalisis terhadap data di memori.
/// Ini implementasi referensi untuk semantik bahasa; provider lain harus memberi hasil yang sama.
/// </summary>
public sealed class InMemoryInterpreter
{
    private readonly InMemoryFunctions _functions;

    public InMemoryInterpreter(InMemoryFunctions? functions = null)
    {
        _functions = functions ?? InMemoryFunctions.CreateDefault();
    }

    /// <summary>
    /// Mengkompilasi lalu menjalankan <paramref name="analyzed"/>. Hasil berupa nilai runtime:
    /// null, bool, string, int, long, decimal, <see cref="IReadOnlyList{T}"/> (array), atau
    /// <see cref="ObjectValue"/>. Urutan hasil dari sebuah sekuens dijamin sama dengan urutan sumber.
    /// Hasil kosong berupa sekuens kosong, bukan null.
    /// </summary>
    public object? Execute(BaseExpression analyzed, ReadOnlyMemory<char> source)
    {
        ArgumentNullException.ThrowIfNull(analyzed);

        var compiler = new InMemoryCompiler(source, _functions);
        var program = compiler.Compile(analyzed);

        return Materialize(program(null));
    }

    private static object? Materialize(object? value) =>
        value switch
        {
            IReadOnlyList<object?> list => list,
            IEnumerable<object?> sequence => new List<object?>(sequence),
            _ => value,
        };
}
