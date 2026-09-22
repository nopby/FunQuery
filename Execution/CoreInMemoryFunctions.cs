using FunQuery.Enums;
using FunQuery.Expressions;

namespace FunQuery.Execution;

internal static class CoreInMemoryFunctions
{
    /// <summary>$source(array): sumber data dari array inline.</summary>
    public static Func<object?, object?> Source(InMemoryCompiler compiler, CallExpression call)
    {
        // $source tidak boleh punya target data (TargetRule.Forbidden), tapi $let boleh
        // mendahuluinya (mis. $let(@x,1).$source(...)) untuk mengikat variable. Rantai itu
        // harus tetap dikompilasi dan dijalankan sekali di sini demi efek sampingnya
        // (mengisi variable), walau nilainya sendiri dibuang. Function lain dengan
        // TargetRule.Forbidden perlu pola yang sama.
        if (call.Target is not null)
        {
            var pass = compiler.Compile(call.Target);
            pass(null);
        }

        return compiler.Compile(call.Arguments[0]);
    }

    /// <summary>
    /// $let(@nama, nilai): mengikat @nama ke nilai (dihitung sekali, dengan element=null) lalu
    /// meneruskan data targetnya tanpa perubahan. Lihat docs/Variables.md.
    /// </summary>
    public static Func<object?, object?> Let(InMemoryCompiler compiler, CallExpression call)
    {
        var targetFn = call.Target is null
            ? static _ => null
            : compiler.Compile(call.Target);

        var name = compiler.GetVariableName((VariableExpression)call.Arguments[0]);

        // Snapshot/restore: $let bersarang di dalam nilai ini sendiri tidak boleh bocor
        // ke luar, sama seperti aturan tipe di SemanticAnalyzer.AnalyzeLet.
        var snapshot = compiler.SnapshotVariables();

        var valueFn = compiler.Compile(call.Arguments[1]);

        // Eager: dihitung sekali sekarang, dengan element=null (tidak ada baris "saat ini"),
        // bukan per baris. Ini yang membuat variable "sudah terwujud" (materialized).
        var value = valueFn(null);

        compiler.RestoreVariables(snapshot);
        compiler.BindVariable(name, value);

        return targetFn;
    }

    /// <summary>$filter(predicate): menyaring elemen. Predikat dihitung terhadap tiap elemen.</summary>
    public static Func<object?, object?> Filter(InMemoryCompiler compiler, CallExpression call)
    {
        var target = compiler.Compile(
            call.Target
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "$filter has no target.",
                call.Function));

        var predicate = compiler.Compile(call.Arguments[0]);

        return element => FilterSequence(
            target(element) as IEnumerable<object?> ?? [],
            predicate);
    }

    private static IEnumerable<object?> FilterSequence(
        IEnumerable<object?> items,
        Func<object?, object?> predicate)
    {
        foreach (var item in items)
        {
            if (ValueOperations.IsTrue(predicate(item)))
                yield return item;
        }
    }
}
