using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery.Execution;

/// <summary>
/// Mengubah baris data mentah dari source terdaftar menjadi nilai runtime FunQuery
/// (<see cref="ObjectValue"/>), sesuai schema yang dideklarasikan. Urutan field mengikuti urutan
/// schema; field yang tidak ada di baris dibaca sebagai null; key tambahan diabaikan.
/// Nilai yang tidak cocok dengan tipe schema ditolak sebagai <c>TYPE_MISMATCH</c>.
/// </summary>
public static class SourceRows
{
    public static IReadOnlyList<object?> Materialize(
        string sourceName,
        ObjectType schema,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);

        var keys = schema.Fields.Keys.ToArray();
        var types = new SemanticType[keys.Length];

        for (int i = 0; i < keys.Length; i++)
            types[i] = schema.Fields[keys[i]];

        var shape = new ObjectShape(keys);
        var result = new List<object?>(rows.Count);

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex]
                ?? throw new QueryException(
                    QueryErrorCode.TypeMismatch,
                    $"Source '{sourceName}' row {rowIndex} is null.");

            var values = new object?[keys.Length];

            for (int i = 0; i < keys.Length; i++)
            {
                row.TryGetValue(keys[i], out var value);

                values[i] = Convert(
                    types[i],
                    value,
                    sourceName,
                    $"row {rowIndex} field '{keys[i]}'");
            }

            result.Add(new ObjectValue(shape, values));
        }

        return result;
    }

    private static object? Convert(
        SemanticType type,
        object? value,
        string sourceName,
        string path)
    {
        if (value is null)
            return null;

        switch (type)
        {
            case ObjectType obj when value is IReadOnlyDictionary<string, object?> nested:
                return CreateObject(obj, nested, sourceName, path);

            case ArrayType array when value is IEnumerable<object?> sequence:
            {
                var list = new List<object?>();
                int index = 0;

                foreach (var item in sequence)
                    list.Add(Convert(array.Type, item, sourceName, AppendIndex(path, index++)));

                return list;
            }

            case IntType when value is int:
            case LongType when value is long:
            case DecimalType when value is decimal:
            case StringType when value is string:
            case BooleanType when value is bool:
            case DateType when value is DateOnly:
            case FloatType when value is float:
            case DoubleType when value is double:
                return value;

            // Tipe yang belum diketahui menerima nilai apa pun, sama seperti di analyzer.
            case AnyType or UnknownType:
                return value;

            default:
                throw Mismatch(type, value, sourceName, path);
        }
    }

    private static ObjectValue CreateObject(
        ObjectType schema,
        IReadOnlyDictionary<string, object?> row,
        string sourceName,
        string path)
    {
        var keys = schema.Fields.Keys.ToArray();
        var values = new object?[keys.Length];

        for (int i = 0; i < keys.Length; i++)
        {
            row.TryGetValue(keys[i], out var value);

            values[i] = Convert(
                schema.Fields[keys[i]],
                value,
                sourceName,
                AppendField(path, keys[i]));
        }

        return new ObjectValue(new ObjectShape(keys), values);
    }

    private static string AppendField(string path, string field) =>
        path.EndsWith('\'')
            ? $"{path[..^1]}.{field}'"
            : $"{path}.{field}";

    private static string AppendIndex(string path, int index) =>
        path.EndsWith('\'')
            ? $"{path[..^1]}[{index}]'"
            : $"{path}[{index}]";

    private static QueryException Mismatch(
        SemanticType expected,
        object? value,
        string sourceName,
        string path) =>
        new(
            QueryErrorCode.TypeMismatch,
            $"Source '{sourceName}' value at {path} expects {expected.Name}, " +
            $"got {RuntimeTypeName(value)}.");

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
            IReadOnlyDictionary<string, object?> => "object",
            IEnumerable<object?> => "array",
            _ => value.GetType().Name,
        };
}
