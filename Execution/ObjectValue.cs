namespace FunQuery.Execution;

/// <summary>
/// Susunan nama field yang dipakai bersama oleh semua object hasil satu literal.
/// </summary>
public sealed class ObjectShape
{
    private readonly Dictionary<string, int> _index;

    public ObjectShape(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        Keys = keys;
        _index = new Dictionary<string, int>(keys.Count, StringComparer.Ordinal);

        for (int i = 0; i < keys.Count; i++)
            _index[keys[i]] = i;
    }

    /// <summary>Nama field sesuai urutan penulisan.</summary>
    public IReadOnlyList<string> Keys { get; }

    public bool TryGetIndex(string name, out int index) =>
        _index.TryGetValue(name, out index);
}

/// <summary>
/// Nilai object pada saat eksekusi. Menyimpan nilai dalam array sesuai <see cref="ObjectShape"/>,
/// tanpa dictionary per baris dan tanpa reflection.
/// </summary>
public sealed class ObjectValue
{
    private readonly object?[] _values;

    public ObjectValue(ObjectShape shape, object?[] values)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length != shape.Keys.Count)
        {
            throw new ArgumentException(
                "The number of values must match the number of fields in the shape.",
                nameof(values));
        }

        Shape = shape;
        _values = values;
    }

    public ObjectShape Shape { get; }

    public int Count => _values.Length;

    public object? GetValueAt(int index) => _values[index];

    /// <summary>Membaca field. Field yang tidak ada dibaca sebagai null.</summary>
    public bool TryGetValue(string name, out object? value)
    {
        if (Shape.TryGetIndex(name, out var index))
        {
            value = _values[index];
            return true;
        }

        value = null;
        return false;
    }
}
