using System.Data.Common;
using System.Globalization;
using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.SemanticTypes;

namespace FunQuery.PostgreSql;

/// <summary>
/// Mengubah hasil reader PostgreSQL menjadi nilai runtime FunQuery. Baris object memakai urutan
/// field schema; kolom yang tidak ada dibaca sebagai null.
/// </summary>
internal static class PostgreSqlRowReader
{
    public static async Task<object?> ReadAsync(
        DbDataReader reader,
        SemanticType resultType,
        CancellationToken cancellationToken)
    {
        if (resultType is ArrayType arrayType)
        {
            var rows = new List<object?>();

            while (await reader.ReadAsync(cancellationToken))
                rows.Add(ReadRow(reader, arrayType.Type));

            return rows;
        }

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadRow(reader, resultType);
    }

    private static object? ReadRow(DbDataReader reader, SemanticType type)
    {
        if (type is ObjectType obj)
        {
            var keys = obj.Fields.Keys.ToArray();
            var values = new object?[keys.Length];
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < reader.FieldCount; i++)
                ordinals[reader.GetName(i)] = i;

            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];

                values[i] = ordinals.TryGetValue(key, out var ordinal)
                    ? ReadValue(reader, ordinal, obj.Fields[key])
                    : null;
            }

            return new ObjectValue(new ObjectShape(keys), values);
        }

        if (type is AnyType or UnknownType or NullType)
            return reader.FieldCount == 0 ? null : ReadValue(reader, 0, type);

        return ReadValue(reader, 0, type);
    }

    private static object? ReadValue(
        DbDataReader reader,
        int ordinal,
        SemanticType type)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        var value = reader.GetValue(ordinal);

        try
        {
            return type switch
            {
                IntType => Convert.ToInt32(value, CultureInfo.InvariantCulture),
                LongType => Convert.ToInt64(value, CultureInfo.InvariantCulture),
                DecimalType => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
                StringType => Convert.ToString(value, CultureInfo.InvariantCulture),
                BooleanType => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
                DateType => ToDateOnly(value),
                FloatType => Convert.ToSingle(value, CultureInfo.InvariantCulture),
                DoubleType => Convert.ToDouble(value, CultureInfo.InvariantCulture),
                AnyType or UnknownType => value,
                NullType => null,
                _ => throw new QueryException(
                    QueryErrorCode.NotSupported,
                    $"Column '{reader.GetName(ordinal)}' has unsupported type '{type.Name}'."),
            };
        }
        catch (Exception error) when (
            error is InvalidCastException or FormatException or OverflowException)
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Column '{reader.GetName(ordinal)}' does not match schema type '{type.Name}'.",
                innerException: error);
        }
    }

    private static DateOnly ToDateOnly(object value) =>
        value switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            string text when DateFormat.TryParse(text, out var date) => date,
            _ => throw new InvalidCastException("Not a date."),
        };
}
