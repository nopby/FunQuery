using FunQuery.Enums;
using FunQuery.Expressions;

namespace FunQuery.Execution;

internal static class CoreInMemoryFunctions
{
    /// <summary>$source(array): sumber data dari array inline.</summary>
    public static Func<object?, object?> Source(InMemoryCompiler compiler, CallExpression call) =>
        compiler.Compile(call.Arguments[0]);

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
