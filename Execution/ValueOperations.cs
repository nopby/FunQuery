namespace FunQuery.Execution;

/// <summary>
/// Perbandingan nilai pada saat eksekusi. Semantiknya mengikuti docs/Operators.md:
/// dua-nilai (tidak ada "unknown"), string ordinal, dan angka dibandingkan sebagai decimal.
/// Semua fungsi total: tidak pernah melempar karena isi nilai.
/// </summary>
public static class ValueOperations
{
    /// <summary>
    /// eq. null hanya sama dengan null. Tipe yang berbeda keluarga tidak pernah sama.
    /// </summary>
    public static bool Equal(object? left, object? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        if (left is string ls)
            return right is string rs && string.Equals(ls, rs, StringComparison.Ordinal);

        if (left is bool lb)
            return right is bool rb && lb == rb;

        if (IsNumber(left) && IsNumber(right))
            return CompareNumbers(left, right) == 0;

        return false;
    }

    /// <summary>
    /// Membandingkan dua nilai yang berurutan (angka atau string).
    /// Mengembalikan false, tanpa hasil, bila salah satunya null atau tidak berurutan.
    /// Dengan begitu gt, gte, lt, dan lte bernilai false untuk null.
    /// </summary>
    public static bool TryCompare(object? left, object? right, out int result)
    {
        result = 0;

        if (left is null || right is null)
            return false;

        if (left is string ls && right is string rs)
        {
            result = string.CompareOrdinal(ls, rs);
            return true;
        }

        if (IsNumber(left) && IsNumber(right))
        {
            result = CompareNumbers(left, right);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Konteks boolean (operand and/or dan predikat $filter): hanya true yang dihitung true.
    /// null dihitung false.
    /// </summary>
    public static bool IsTrue(object? value) => value is true;

    private static bool IsNumber(object value) =>
        value is int or long or decimal or double or float;

    private static int CompareNumbers(object left, object right)
    {
        // float dan double tidak bisa dijadikan decimal tanpa kehilangan rentang.
        if (left is double or float || right is double or float)
            return ToDouble(left).CompareTo(ToDouble(right));

        return ToDecimal(left).CompareTo(ToDecimal(right));
    }

    private static decimal ToDecimal(object value) =>
        value switch
        {
            int i => i,
            long l => l,
            decimal d => d,
            _ => throw new InvalidOperationException("Not an exact number."),
        };

    private static double ToDouble(object value) =>
        value switch
        {
            int i => i,
            long l => l,
            decimal d => (double)d,
            double d => d,
            float f => f,
            _ => throw new InvalidOperationException("Not a number."),
        };
}
