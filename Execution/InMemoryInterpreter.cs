using FunQuery.Enums;
using FunQuery.Expressions;

namespace FunQuery.Execution;

/// <summary>
/// Provider in-memory: mengeksekusi query yang sudah dianalisis terhadap data di memori.
/// Ini implementasi referensi untuk semantik bahasa; provider lain harus memberi hasil yang sama.
/// </summary>
public sealed class InMemoryInterpreter
{
    private readonly InMemoryFunctions _functions;
    private readonly QueryLimits _limits;

    public InMemoryInterpreter(
        InMemoryFunctions? functions = null,
        QueryLimits? limits = null)
    {
        _functions = functions ?? InMemoryFunctions.CreateDefault();
        _limits = limits ?? QueryLimits.Default;
    }

    /// <summary>
    /// Mengkompilasi lalu menjalankan <paramref name="analyzed"/>. Hasil berupa nilai runtime:
    /// null, bool, string, int, long, decimal, <see cref="IReadOnlyList{T}"/> (array), atau
    /// <see cref="ObjectValue"/>. Urutan hasil dari sebuah sekuens dijamin sama dengan urutan sumber.
    /// Hasil kosong berupa sekuens kosong, bukan null.
    /// </summary>
    public object? Execute(
        BaseExpression analyzed,
        ReadOnlyMemory<char> source,
        IReadOnlyDictionary<string, object?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analyzed);

        var guard = new ExecutionGuard(_limits.MaxExecutionTime, cancellationToken);
        var compiler = new InMemoryCompiler(source, _functions, variables, guard);
        var program = compiler.Compile(analyzed);

        return Materialize(program(null));
    }

    private object? Materialize(object? value) =>
        value switch
        {
            IReadOnlyList<object?> list => CheckSize(list),
            IEnumerable<object?> sequence => CheckSize(new List<object?>(sequence)),
            _ => value,
        };

    private IReadOnlyList<object?> CheckSize(IReadOnlyList<object?> items)
    {
        if (items.Count > _limits.MaxResultItems)
        {
            throw new QueryException(
                QueryErrorCode.ResultTooLarge,
                $"The query returned more than {_limits.MaxResultItems} items.");
        }

        return items;
    }
}
