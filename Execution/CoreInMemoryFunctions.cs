using FunQuery.Enums;
using FunQuery.Expressions;

namespace FunQuery.Execution;

/// <summary>
/// Kotak mutable berisi posisi elemen saat ini, dibaca $index(). Satu instance dibuat per
/// function element-scoped yang dikompilasi (lihat InMemoryCompiler.CompileIndexed), dan diisi
/// ulang oleh function itu sendiri sebelum memanggil delegate untuk tiap elemen.
/// </summary>
internal sealed class IndexCell
{
    public int Value;
}

internal static class CoreInMemoryFunctions
{
    /// <summary>$index() atau $index(base): posisi elemen pada function pembungkus terdekat.</summary>
    public static Func<object?, object?> Index(InMemoryCompiler compiler, CallExpression call)
    {
        var cell = compiler.CurrentIndexCell
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "$index has no active index cell; the analyzer should have rejected this " +
                "as ITEM_OUT_OF_CONTEXT.",
                call.Function);

        var baseValue = 0;

        if (call.Arguments.Count == 1)
        {
            // Analyzer sudah memastikan bentuknya int literal atau @variable. Literal dan
            // variable sama-sama dibaca sekali di sini (bukan per elemen). Variable yang
            // ternyata bukan int — analyzer bisa saja hanya melihat tipenya yang dideklarasikan
            // — ditolak sebagai TYPE_MISMATCH, bukan cast mentah yang melempar InvalidCast.
            var baseFn = compiler.Compile(call.Arguments[0]);

            baseValue = baseFn(null) switch
            {
                int value => value,
                _ => throw new QueryException(
                    QueryErrorCode.TypeMismatch,
                    "The variable given to '$index' must resolve to an int base.",
                    call.Arguments[0].Span),
            };
        }

        return _ =>
        {
            // Posisi + base tidak boleh diam-diam wrap: overflow menjadi error yang bisa
            // dilaporkan, dengan span pemanggilan $index ini sendiri.
            try
            {
                return checked(cell.Value + baseValue);
            }
            catch (OverflowException)
            {
                throw new QueryException(
                    QueryErrorCode.NumberOutOfRange,
                    "$index() overflowed: the element position plus the base does not fit " +
                    "in the int range.",
                    call.Span);
            }
        };
    }


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

    /// <summary>
    /// $filter(predicate): menyaring elemen. Predikat dihitung terhadap tiap elemen.
    /// Hasilnya dimaterialisasi, sama seperti $take/$skip/$sort: setiap nilai array di runtime
    /// adalah IReadOnlyList&lt;object?&gt;, jadi $distinct membandingkannya secara struktural
    /// dan $let menyimpan nilai (bukan query yang dievaluasi ulang). Lihat docs/Functions.md.
    /// </summary>
    public static Func<object?, object?> Filter(InMemoryCompiler compiler, CallExpression call)
    {
        var target = compiler.Compile(
            call.Target
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "$filter has no target.",
                call.Function));

        var (predicate, cell) = compiler.CompileIndexed(call.Arguments[0]);

        return element => FilterSequence(
            target(element) as IEnumerable<object?> ?? [],
            predicate,
            cell,
            compiler.Step).ToList();
    }

    private static IEnumerable<object?> FilterSequence(
        IEnumerable<object?> items,
        Func<object?, object?> predicate,
        IndexCell cell,
        Action step)
    {
        int index = 0;

        foreach (var item in items)
        {
            step();
            cell.Value = index++;

            if (ValueOperations.IsTrue(predicate(item)))
                yield return item;
        }
    }

    /// <summary>$map(expression): memproyeksikan setiap elemen lewat satu ekspresi.
    /// Hasilnya dimaterialisasi seperti $filter (lihat catatan di sana).</summary>
    public static Func<object?, object?> Map(InMemoryCompiler compiler, CallExpression call)
    {
        var target = compiler.Compile(
            call.Target
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "$map has no target.",
                call.Function));

        var (projector, cell) = compiler.CompileIndexed(call.Arguments[0]);

        return element => Project(target(element) as IEnumerable<object?> ?? [], projector, cell, compiler.Step)
            .ToList();
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

        // Object form ($select({...})) sudah satu ekspresi tunggal, jadi CompileIndexed langsung
        // dipakai sama seperti $map. Bentuk daftar ($select(id, name)) berisi beberapa argumen,
        // dan semuanya harus berbagi SATU cell yang sama (posisi elemen sama untuk tiap field
        // pada baris yang sama), jadi cell dibuat sendiri lalu tiap argumen dikompilasi di
        // dalam scope cell itu lewat CompileIndexed bersarang secara manual (push sekali,
        // compile semua argumen, pop sekali) di CompileFieldList.
        var (projector, cell) = call.Arguments is [BlockExpression]
            ? compiler.CompileIndexed(call.Arguments[0])
            : CompileFieldList(compiler, call.Arguments);

        return element => Project(target(element) as IEnumerable<object?> ?? [], projector, cell, compiler.Step)
            .ToList();
    }

    private static (Func<object?, object?> Projector, IndexCell Cell) CompileFieldList(
        InMemoryCompiler compiler,
        IReadOnlyList<BaseExpression> arguments)
    {
        var keys = new string[arguments.Count];
        var values = new Func<object?, object?>[arguments.Count];

        // Satu cell dipakai bersama oleh semua argumen: tiap field pada baris yang sama
        // berada di posisi yang sama, jadi $index() di salah satu argumen harus melihat
        // nilai yang sama dengan $index() di argumen lain pada pemanggilan yang sama.
        var cell = compiler.PushIndexCell();

        for (int i = 0; i < arguments.Count; i++)
        {
            keys[i] = compiler.GetSelectKey(arguments[i]);
            values[i] = compiler.Compile(arguments[i]);
        }

        compiler.PopIndexCell();

        var shape = new ObjectShape(keys);

        Func<object?, object?> projector = item =>
        {
            var row = new object?[values.Length];

            for (int i = 0; i < row.Length; i++)
                row[i] = values[i](item);

            return new ObjectValue(shape, row);
        };

        return (projector, cell);
    }

    /// <summary>Menerapkan sebuah proyektor ke tiap elemen sekuens, dipakai $map dan $select.</summary>
    private static IEnumerable<object?> Project(
        IEnumerable<object?> items,
        Func<object?, object?> projector,
        IndexCell cell,
        Action step)
    {
        int index = 0;

        foreach (var item in items)
        {
            step();
            cell.Value = index++;
            yield return projector(item);
        }
    }

    /// <summary>
    /// $sort(key) atau $sort(key, asc|desc): mengurutkan berdasarkan satu key per elemen.
    /// null selalu dianggap paling kecil (asc: di awal, desc: di akhir). Index elemen asli
    /// dijadikan pemutus akhir, sehingga perbandingan tidak pernah menghasilkan "sama" dan
    /// hasilnya selalu stabil (elemen dengan key sama tetap dalam urutan asalnya) walau
    /// Array.Sort sendiri tidak menjamin stabilitas untuk perbandingan yang benar-benar seri.
    /// </summary>
    public static Func<object?, object?> Sort(InMemoryCompiler compiler, CallExpression call)
    {
        var target = compiler.Compile(
            call.Target
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "$sort has no target.",
                call.Function));

        var (key, cell) = compiler.CompileIndexed(call.Arguments[0]);

        var descending = call.Arguments.Count == 2 &&
            compiler.GetIdentifierText(call.Arguments[1]) == "desc";

        return element =>
        {
            compiler.Step();
            var items = (target(element) as IEnumerable<object?> ?? []).ToList();
            var entries = new (object? Item, bool IsNull, object? Value, int Index)[items.Count];

            for (int i = 0; i < items.Count; i++)
            {
                compiler.Step();
                // $index() di dalam key $sort merujuk posisi sebelum pengurutan.
                cell.Value = i;
                var value = key(items[i]);
                entries[i] = (items[i], value is null, value, i);
            }

            Array.Sort(entries, (a, b) => CompareSortKeys(a, b, descending));
            compiler.Step();

            var result = new List<object?>(entries.Length);

            foreach (var entry in entries)
                result.Add(entry.Item);

            return result;
        };
    }

    private static int CompareSortKeys(
        (object? Item, bool IsNull, object? Value, int Index) x,
        (object? Item, bool IsNull, object? Value, int Index) y,
        bool descending)
    {
        if (x.IsNull != y.IsNull)
        {
            var nullFirst = x.IsNull ? -1 : 1;
            return descending ? -nullFirst : nullFirst;
        }

        if (!x.IsNull && ValueOperations.TryCompare(x.Value, y.Value, out var comparison) &&
            comparison != 0)
        {
            return descending ? -comparison : comparison;
        }

        // Pemutus akhir: index asli, selalu menaik terlepas dari arah, supaya hasil stabil.
        return x.Index.CompareTo(y.Index);
    }

    /// <summary>$take(count): mengambil sejumlah elemen pertama. Sejalan dengan LINQ, count
    /// negatif memberi sekuens kosong.</summary>
    public static Func<object?, object?> Take(InMemoryCompiler compiler, CallExpression call)
    {
        var target = compiler.Compile(
            call.Target
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "$take has no target.",
                call.Function));

        var count = compiler.Compile(call.Arguments[0]);

        return element =>
        {
            compiler.Step();

            return (target(element) as IEnumerable<object?> ?? [])
                .Take(ToInt32(count(element)))
                .ToList();
        };
    }

    /// <summary>$skip(count): melewati sejumlah elemen pertama. Sejalan dengan LINQ, count
    /// negatif sama dengan tidak melewati apa pun.</summary>
    public static Func<object?, object?> Skip(InMemoryCompiler compiler, CallExpression call)
    {
        var target = compiler.Compile(
            call.Target
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "$skip has no target.",
                call.Function));

        var count = compiler.Compile(call.Arguments[0]);

        return element =>
        {
            compiler.Step();

            return (target(element) as IEnumerable<object?> ?? [])
                .Skip(ToInt32(count(element)))
                .ToList();
        };
    }

    /// <summary>$count(): jumlah elemen sebagai long.</summary>
    public static Func<object?, object?> Count(InMemoryCompiler compiler, CallExpression call)
    {
        var target = TargetOf(compiler, call);

        return element =>
        {
            compiler.Step();

            return target(element) switch
            {
                IReadOnlyCollection<object?> collection => (long)collection.Count,
                IEnumerable<object?> sequence => (long)sequence.Count(),
                _ => 0L,
            };
        };
    }

    /// <summary>$any() atau $any(predicate): true bila ada elemen (yang memenuhi predikat).</summary>
    public static Func<object?, object?> Any(InMemoryCompiler compiler, CallExpression call)
    {
        var target = TargetOf(compiler, call);
        var (predicate, cell) = CompilePredicateOrNull(compiler, call);

        return element =>
        {
            int index = 0;

            foreach (var item in target(element) as IEnumerable<object?> ?? [])
            {
                compiler.Step();

                if (cell is not null)
                    cell.Value = index++;

                if (predicate is null || ValueOperations.IsTrue(predicate(item)))
                    return true;
            }

            return false;
        };
    }

    /// <summary>$first() atau $first(predicate): elemen pertama (yang memenuhi predikat), atau null.</summary>
    public static Func<object?, object?> First(InMemoryCompiler compiler, CallExpression call)
    {
        var target = TargetOf(compiler, call);
        var (predicate, cell) = CompilePredicateOrNull(compiler, call);

        return element =>
        {
            int index = 0;

            foreach (var item in target(element) as IEnumerable<object?> ?? [])
            {
                compiler.Step();

                if (cell is not null)
                    cell.Value = index++;

                if (predicate is null || ValueOperations.IsTrue(predicate(item)))
                    return item;
            }

            return null;
        };
    }

    private static (Func<object?, object?>? Predicate, IndexCell? Cell) CompilePredicateOrNull(
        InMemoryCompiler compiler,
        CallExpression call)
    {
        if (call.Arguments.Count == 0)
            return (null, null);

        var (predicate, cell) = compiler.CompileIndexed(call.Arguments[0]);

        return (predicate, cell);
    }

    /// <summary>
    /// $distinct() atau $distinct(key): membuang duplikat, elemen pertama yang dipertahankan dan
    /// urutan asal dijaga. Tanpa key, seluruh elemen dibandingkan secara struktural; dengan key,
    /// hanya nilai key itu yang dibandingkan. Lihat ValueEqualityComparer.
    /// </summary>
    public static Func<object?, object?> Distinct(InMemoryCompiler compiler, CallExpression call)
    {
        var target = TargetOf(compiler, call);
        var (key, cell) = CompilePredicateOrNull(compiler, call);

        return element =>
        {
            var seen = new HashSet<object?>(ValueEqualityComparer.Instance);
            var result = new List<object?>();
            int index = 0;

            foreach (var item in target(element) as IEnumerable<object?> ?? [])
            {
                compiler.Step();

                if (cell is not null)
                    cell.Value = index++;

                if (seen.Add(key is null ? item : key(item)))
                    result.Add(item);
            }

            return result;
        };
    }

    /// <summary>$lower(text): huruf kecil secara invariant. null tetap null.</summary>
    public static Func<object?, object?> Lower(InMemoryCompiler compiler, CallExpression call)
    {
        var text = compiler.Compile(call.Arguments[0]);

        return element => text(element) switch
        {
            null => null,
            string value => value.ToLowerInvariant(),
            var other => throw RuntimeTypeError(call, "$lower", "string", other),
        };
    }

    /// <summary>$upper(text): huruf besar secara invariant. null tetap null.</summary>
    public static Func<object?, object?> Upper(InMemoryCompiler compiler, CallExpression call)
    {
        var text = compiler.Compile(call.Arguments[0]);

        return element => text(element) switch
        {
            null => null,
            string value => value.ToUpperInvariant(),
            var other => throw RuntimeTypeError(call, "$upper", "string", other),
        };
    }

    /// <summary>
    /// $length(text): jumlah UTF-16 code unit, satuan yang sama dengan pengurutan string
    /// (lihat docs/DataTypes.md#strings). null tetap null, bukan 0.
    /// </summary>
    public static Func<object?, object?> Length(InMemoryCompiler compiler, CallExpression call)
    {
        var text = compiler.Compile(call.Arguments[0]);

        return element => text(element) switch
        {
            null => null,
            string value => (long)value.Length,
            var other => throw RuntimeTypeError(call, "$length", "string", other),
        };
    }

    /// <summary>
    /// $coalesce(a, b, ...): nilai pertama yang bukan null, atau null bila semuanya null.
    /// Argumen dihitung lazy dan berurutan: argumen berikutnya tidak dievaluasi setelah ada
    /// nilai yang bukan null, sama seperti operator ?? di C#.
    /// </summary>
    public static Func<object?, object?> Coalesce(InMemoryCompiler compiler, CallExpression call)
    {
        var arguments = call.Arguments.Select(compiler.Compile).ToArray();

        return element =>
        {
            foreach (var argument in arguments)
            {
                var value = argument(element);

                if (value is not null)
                    return value;
            }

            return null;
        };
    }

    /// <summary>
    /// $date('yyyy-MM-dd'): parse teks tanggal menjadi date. Formatnya satu-satunya yang
    /// diterima bahasa (lihat DateFormat). null tetap null; teks yang salah menjadi
    /// INVALID_DATE, bukan exception mentah dari DateOnly.
    /// </summary>
    public static Func<object?, object?> Date(InMemoryCompiler compiler, CallExpression call)
    {
        var text = compiler.Compile(call.Arguments[0]);

        return element =>
        {
            var value = text(element);

            if (value is null)
                return null;

            if (value is not string s)
                throw RuntimeTypeError(call, "$date", "string", value);

            if (!DateFormat.TryParse(s, out var date))
                throw new QueryException(
                    QueryErrorCode.InvalidDate,
                    $"'{s}' is not a valid date. Use the ISO format yyyy-MM-dd.",
                    call.Arguments[0].Span);

            return date;
        };
    }

    /// <summary>
    /// Argumen fungsi skalar bisa bertipe any saat analisis (mis. $field(@kolom)), jadi tipe
    /// sebenarnya baru diperiksa di sini. Error tetap TYPE_MISMATCH dengan posisi argumen.
    /// </summary>
    private static QueryException RuntimeTypeError(
        CallExpression call,
        string function,
        string expected,
        object? value) =>
        new(
            QueryErrorCode.TypeMismatch,
            $"Argument 1 of '{function}' expects {expected} at run time, got {RuntimeTypeName(value)}.",
            call.Arguments[0].Span);

    private static string RuntimeTypeName(object? value) =>
        value switch
        {
            null => "null",
            string => "string",
            bool => "bool",
            int => "int",
            long => "long",
            decimal => "decimal",
            double => "double",
            float => "float",
            DateOnly => "date",
            ObjectValue => "object",
            IEnumerable<object?> => "array",
            _ => value.GetType().Name,
        };

    private static Func<object?, object?> TargetOf(InMemoryCompiler compiler, CallExpression call) =>
        compiler.Compile(
            call.Target
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                $"{compiler.GetFunctionText(call)} has no target.",
                call.Function));

    private static int ToInt32(object? value) =>
        value switch
        {
            int i => i,
            long l => l > int.MaxValue ? int.MaxValue : l < int.MinValue ? int.MinValue : (int)l,
            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                "$take/$skip's count did not evaluate to int or long."),
        };
}
