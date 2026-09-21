using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FunQuery.Enums;

namespace FunQuery.Execution;

/// <summary>
/// Menulis nilai runtime sebagai JSON dengan <see cref="Utf8JsonWriter"/>. Tidak memakai reflection
/// maupun serializer generik, sehingga aman untuk Native AOT.
/// </summary>
public static class ValueJson
{
    private static readonly JsonWriterOptions Options = new()
    {
        // Jangan meng-escape karakter non-ASCII atau tanda kutip tunggal tanpa perlu.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(object? value)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            Write(writer, value);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;

            case bool b:
                writer.WriteBooleanValue(b);
                break;

            case string s:
                writer.WriteStringValue(s);
                break;

            case int i:
                writer.WriteNumberValue(i);
                break;

            case long l:
                writer.WriteNumberValue(l);
                break;

            case decimal d:
                writer.WriteNumberValue(d);
                break;

            case double d:
                writer.WriteNumberValue(d);
                break;

            case float f:
                writer.WriteNumberValue(f);
                break;

            case ObjectValue row:
                writer.WriteStartObject();

                for (int i = 0; i < row.Count; i++)
                {
                    writer.WritePropertyName(row.Shape.Keys[i]);
                    Write(writer, row.GetValueAt(i));
                }

                writer.WriteEndObject();
                break;

            case IEnumerable<object?> sequence:
                writer.WriteStartArray();

                foreach (var item in sequence)
                    Write(writer, item);

                writer.WriteEndArray();
                break;

            default:
                throw new QueryException(
                    QueryErrorCode.InternalError,
                    $"Cannot write a value of type {value.GetType().Name} as JSON.");
        }
    }
}
