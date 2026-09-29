namespace FunQuery.Execution;

/// <summary>
/// Kesamaan struktural nilai runtime, dipakai $distinct. Berbeda dari operator eq (yang
/// menolak array dan object): di sini array dan object dibandingkan isinya. Angka dibandingkan
/// menurut nilainya lintas tipe (1 sama dengan 1.0), string ordinal, null hanya sama dengan null.
/// Object dianggap sama bila kumpulan field-nya sama (urutan tidak berpengaruh) dan setiap
/// nilainya sama; field yang hilang TIDAK sama dengan field bernilai null.
/// </summary>
public sealed class ValueEqualityComparer : IEqualityComparer<object?>
{
    public static readonly ValueEqualityComparer Instance = new();

    bool IEqualityComparer<object?>.Equals(object? x, object? y) => AreEqual(x, y);

    int IEqualityComparer<object?>.GetHashCode(object? value) => Hash(value);

    public static bool AreEqual(object? x, object? y)
    {
        if (x is null || y is null)
            return x is null && y is null;

        if (x is ObjectValue ox)
            return y is ObjectValue oy && ObjectsEqual(ox, oy);

        if (x is IReadOnlyList<object?> lx)
            return y is IReadOnlyList<object?> ly && ListsEqual(lx, ly);

        return ValueOperations.Equal(x, y);
    }

    private static bool ObjectsEqual(ObjectValue x, ObjectValue y)
    {
        if (x.Count != y.Count)
            return false;

        for (int i = 0; i < x.Count; i++)
        {
            if (!y.TryGetValue(x.Shape.Keys[i], out var other) ||
                !AreEqual(x.GetValueAt(i), other))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ListsEqual(IReadOnlyList<object?> x, IReadOnlyList<object?> y)
    {
        if (x.Count != y.Count)
            return false;

        for (int i = 0; i < x.Count; i++)
        {
            if (!AreEqual(x[i], y[i]))
                return false;
        }

        return true;
    }

    /// <summary>Hash yang konsisten dengan <see cref="AreEqual"/>: nilai yang sama pasti sama hash-nya.</summary>
    public static int Hash(object? value)
    {
        switch (value)
        {
            case null:
                return 0;

            case string s:
                return s.GetHashCode();

            case bool b:
                return b ? 1 : 2;

            case int or long or decimal or double or float:
                return NumberHash(value);

            case ObjectValue row:
            {
                // Dijumlahkan, bukan digabung berurutan, karena urutan field tidak berpengaruh.
                var hash = 17;

                for (int i = 0; i < row.Count; i++)
                    hash += HashCode.Combine(row.Shape.Keys[i], Hash(row.GetValueAt(i)));

                return hash;
            }

            case IReadOnlyList<object?> list:
            {
                var hash = new HashCode();

                foreach (var item in list)
                    hash.Add(Hash(item));

                return hash.ToHashCode();
            }

            default:
                return 0;
        }
    }

    // Semua angka di-hash lewat double, karena 1, 1L, 1.0m, dan 1.0 harus sama hash-nya.
    // Tabrakan hash untuk angka besar yang berbeda tidak masalah: yang menentukan tetap AreEqual.
    private static int NumberHash(object value)
    {
        var number = value switch
        {
            int i => (double)i,
            long l => (double)l,
            decimal m => (double)m,
            double d => d,
            float f => (double)f,
            _ => 0d,
        };

        if (number == 0)
            number = 0; // 0.0 dan -0.0 sama nilainya, jadi harus sama hash-nya

        return number.GetHashCode();
    }
}
