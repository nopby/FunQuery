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

    /// <summary>
    /// $field(name) atau $field(@variable): membaca field dari elemen saat ini lewat path
    /// bertitik (mis. "address.city"). Path diketahui sekali di sini (baik dari literal
    /// maupun dari nilai variable yang sudah terikat), lalu dipakai sebagai segmen tetap
    /// untuk setiap baris. Field atau segmen yang hilang dibaca sebagai null, sama seperti
    /// FieldAccessExpression biasa. Lihat docs/FieldAccess.md.
    /// </summary>
    public static Func<object?, object?> Field(InMemoryCompiler compiler, CallExpression call)
    {
        var argument = call.Arguments[0];

        var path = argument switch
        {
            ValueExpression value => compiler.GetStringLiteralValue(value),

            VariableExpression variable =>
                compiler.ResolveVariable(compiler.GetVariableName(variable)) as string
                ?? throw new QueryException(
                    QueryErrorCode.InvalidFieldArgument,
                    "The variable given to '$field' must resolve to a string field name or path.",
                    variable.Span),

            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                "$field's argument must be a string literal or a variable."),
        };

        var segments = path.Split('.');

        return element =>
        {
            object? current = element;

            foreach (var segment in segments)
            {
                current = current is ObjectValue row && row.TryGetValue(segment, out var value)
                    ? value
                    : null;
            }

            return current;
        };
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

    /// <summary>$map(expression): memproyeksikan setiap elemen lewat satu ekspresi.</summary>
    public static Func<object?, object?> Map(InMemoryCompiler compiler, CallExpression call)
    {
        var target = compiler.Compile(
            call.Target
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "$map has no target.",
                call.Function));

        var projector = compiler.Compile(call.Arguments[0]);

        return element => Project(target(element) as IEnumerable<object?> ?? [], projector);
    }

    /// <summary>
    /// $select(id, name) atau $select({...}): sama seperti $map, bedanya proyektor dibangun
    /// dari nama field (bentuk daftar) atau dari literal object (bentuk object) yang sudah
    /// dianalisis sebelumnya. Lihat SemanticAnalyzer.AnalyzeSelect.
    /// </summary>
    public static Func<object?, object?> Select(InMemoryCompiler compiler, CallExpression call)
    {
        var target = compiler.Compile(
            call.Target
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "$select has no target.",
                call.Function));

        var projector = call.Arguments is [BlockExpression]
            ? compiler.Compile(call.Arguments[0])
            : CompileFieldList(compiler, call.Arguments);

        return element => Project(target(element) as IEnumerable<object?> ?? [], projector);
    }

    private static Func<object?, object?> CompileFieldList(
        InMemoryCompiler compiler,
        IReadOnlyList<BaseExpression> arguments)
    {
        var keys = new string[arguments.Count];
        var values = new Func<object?, object?>[arguments.Count];

        for (int i = 0; i < arguments.Count; i++)
        {
            keys[i] = compiler.GetSelectKey(arguments[i]);
            values[i] = compiler.Compile(arguments[i]);
        }

        var shape = new ObjectShape(keys);

        return item =>
        {
            var row = new object?[values.Length];

            for (int i = 0; i < row.Length; i++)
                row[i] = values[i](item);

            return new ObjectValue(shape, row);
        };
    }

    /// <summary>Menerapkan sebuah proyektor ke tiap elemen sekuens, dipakai $map dan $select.</summary>
    private static IEnumerable<object?> Project(
        IEnumerable<object?> items,
        Func<object?, object?> projector)
    {
        foreach (var item in items)
            yield return projector(item);
    }
}
