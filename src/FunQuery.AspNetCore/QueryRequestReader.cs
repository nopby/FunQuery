using System.Text.Json;
using FunQuery.Enums;
using Microsoft.AspNetCore.Http;

namespace FunQuery.AspNetCore;

/// <summary>
/// Membaca dan memvalidasi body <c>POST /query</c>: Content-Type, ukuran, JSON, dan variabel.
/// Semua kegagalan menjadi <see cref="QueryException"/> yang dipetakan filter error yang sama
/// dengan GET.
/// </summary>
internal static class QueryRequestReader
{
    private const int BufferSize = 8192;

    public static async Task<QueryRequest> ReadAsync(
        HttpContext context,
        FunQueryOptions options)
    {
        if (!context.Request.HasJsonContentType())
        {
            throw new QueryException(
                QueryErrorCode.UnsupportedMediaType,
                "Content-Type must be application/json.");
        }

        if (context.Request.ContentLength is long contentLength &&
            contentLength > options.MaxRequestBodyBytes)
        {
            throw TooLarge(options);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        int read;

        while ((read = await context.Request.Body.ReadAsync(
                   chunk.AsMemory(),
                   context.RequestAborted)) > 0)
        {
            if (buffer.Length + read > options.MaxRequestBodyBytes)
                throw TooLarge(options);

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;

        QueryRequest? request;

        try
        {
            request = await JsonSerializer.DeserializeAsync(
                buffer,
                FunQueryJsonContext.Default.QueryRequest,
                context.RequestAborted);
        }
        catch (JsonException)
        {
            // Jangan sertakan isi body atau detail parser di pesan.
            throw new QueryException(
                QueryErrorCode.InvalidRequest,
                "The request body is not valid JSON.");
        }

        return request
            ?? throw new QueryException(
                QueryErrorCode.InvalidRequest,
                "A request body is required.");
    }

    public static QueryEnvironment ReadVariables(QueryRequest request, QueryLimits limits)
    {
        if (request.Variables is not { } variables || variables.ValueKind == JsonValueKind.Null)
            return QueryEnvironment.Empty;

        if (variables.ValueKind != JsonValueKind.Object)
        {
            throw new QueryException(
                QueryErrorCode.InvalidRequest,
                "The 'variables' field must be a JSON object.");
        }

        var parameters = new Dictionary<string, QueryParameter>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in variables.EnumerateObject())
        {
            var name = property.Name;
            QueryVariableValues.ValidateName(name);

            if (!seen.Add(name))
            {
                throw QueryVariableValues.InvalidParameter(
                    $"Query variable '{name}' is supplied more than once.");
            }

            if (seen.Count > limits.MaxParameters)
            {
                throw QueryVariableValues.InvalidParameter(
                    $"Too many query variables; at most {limits.MaxParameters} are allowed.");
            }

            if (QueryVariableValues.ValueLength(property.Value) >
                limits.MaxParameterValueLength)
            {
                throw QueryVariableValues.InvalidParameter(
                    $"Query variable '{name}' is longer than " +
                    $"{limits.MaxParameterValueLength} characters.");
            }

            parameters[name] = QueryVariableValues.FromJsonElement(name, property.Value);
        }

        return new QueryEnvironment(parameters);
    }

    private static QueryException TooLarge(FunQueryOptions options) =>
        new(
            QueryErrorCode.RequestBodyTooLarge,
            $"The request body is larger than {options.MaxRequestBodyBytes} bytes.");
}
