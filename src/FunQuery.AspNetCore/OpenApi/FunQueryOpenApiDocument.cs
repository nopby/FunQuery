using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FunQuery.AspNetCore;

/// <summary>
/// OpenAPI 3.1 document untuk endpoint FunQuery. Dokumen dibangun langsung sebagai JsonNode,
/// bukan lewat reflection/endpoint metadata, sehingga tetap aman untuk Native AOT dan tidak
/// bergantung pada bentuk delegate handler.
/// </summary>
internal static class FunQueryOpenApiDocument
{
    public static string Create(
        string route,
        string queryParameter,
        string sourceParameter)
    {
        var document = new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "FunQuery HTTP API",
                ["version"] = "1.0.0",
                ["description"] =
                    "Execute FunQuery expressions over inline data or a registered source. " +
                    "Variables are supplied as `v.<name>` on GET or in the variables object on POST.",
            },
            ["servers"] = Array(
                new JsonObject { ["url"] = "/" }),
            ["tags"] = Array(
                new JsonObject
                {
                    ["name"] = "Query",
                    ["description"] = "Execute FunQuery expressions.",
                }),
            ["paths"] = new JsonObject
            {
                [route] = new JsonObject
                {
                    ["get"] = GetOperation(queryParameter, sourceParameter),
                    ["post"] = PostOperation(),
                },
            },
            ["components"] = Components(),
        };

        return document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static JsonObject GetOperation(
        string queryParameter,
        string sourceParameter) =>
        new()
        {
            ["tags"] = Array(JsonValue.Create("Query")),
            ["summary"] = "Execute a FunQuery expression",
            ["description"] =
                "Runs a query against an inline source or a registered source. " +
                "The selected source is bound to @source. Query variables use `v.<name>`.",
            ["operationId"] = "executeQueryGet",
            ["parameters"] = Array(
                new JsonObject
                {
                    ["name"] = queryParameter,
                    ["in"] = "query",
                    ["required"] = true,
                    ["description"] = "The FunQuery expression.",
                    ["example"] = "$source([1, 2, 3]).$count()",
                    ["schema"] = new JsonObject { ["type"] = "string" },
                },
                new JsonObject
                {
                    ["name"] = sourceParameter,
                    ["in"] = "query",
                    ["required"] = false,
                    ["description"] =
                        "Name of a registered source; binds its rows to @source.",
                    ["example"] = "orders",
                    ["schema"] = new JsonObject { ["type"] = "string" },
                }),
            ["responses"] = Responses(),
        };

    private static JsonObject PostOperation() =>
        new()
        {
            ["tags"] = Array(JsonValue.Create("Query")),
            ["summary"] = "Execute a FunQuery expression from a JSON body",
            ["description"] =
                "Same pipeline as GET. Use this for larger queries and typed variables.",
            ["operationId"] = "executeQueryPost",
            ["requestBody"] = new JsonObject
            {
                ["required"] = true,
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject
                    {
                        ["schema"] = new JsonObject
                        {
                            ["$ref"] = "#/components/schemas/QueryRequest",
                        },
                        ["examples"] = new JsonObject
                        {
                            ["count"] = new JsonObject
                            {
                                ["summary"] = "Count inline rows",
                                ["value"] = new JsonObject
                                {
                                    ["query"] = "$source([1, 2, 3]).$count()",
                                },
                            },
                            ["sourceAndVariable"] = new JsonObject
                            {
                                ["summary"] = "Filter a registered source",
                                ["value"] = new JsonObject
                                {
                                    ["query"] =
                                        "$source(@source).$filter(id gt @min).$count()",
                                    ["source"] = "orders",
                                    ["variables"] = new JsonObject
                                    {
                                        ["min"] = 1,
                                    },
                                },
                            },
                        },
                    },
                },
            },
            ["responses"] = Responses(),
        };

    private static JsonObject Responses() =>
        new()
        {
            ["200"] = new JsonObject
            {
                ["description"] =
                    "Query result as JSON. The shape depends on the query.",
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject
                    {
                        ["schema"] = new JsonObject
                        {
                            ["description"] = "Any JSON value produced by the query.",
                        },
                        ["examples"] = new JsonObject
                        {
                            ["rows"] = new JsonObject
                            {
                                ["summary"] = "A filtered projection",
                                ["value"] = Array(
                                    new JsonObject
                                    {
                                        ["id"] = 1,
                                        ["customer"] = "Ana",
                                    }),
                            },
                        },
                    },
                },
            },
            ["400"] = ErrorResponse("The query or request is invalid."),
            ["408"] = ErrorResponse("The query exceeded its time budget."),
            ["413"] = ErrorResponse("The query string or request body is too large."),
            ["415"] = ErrorResponse("The POST body is not JSON."),
            ["422"] = ErrorResponse("The result or source is too large."),
            ["500"] = ErrorResponse("The query could not be executed."),
        };

    private static JsonObject ErrorResponse(string description) =>
        new()
        {
            ["description"] = description,
            ["content"] = new JsonObject
            {
                ["application/json"] = new JsonObject
                {
                    ["schema"] = new JsonObject
                    {
                        ["$ref"] = "#/components/schemas/QueryErrorResponse",
                    },
                },
            },
        };

    private static JsonObject Components() =>
        new()
        {
            ["schemas"] = new JsonObject
            {
                ["QueryRequest"] = new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = Array(JsonValue.Create("query")),
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The FunQuery expression.",
                        },
                        ["source"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] =
                                "Name of a registered source; binds its rows to @source.",
                        },
                        ["variables"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["description"] =
                                "Variables for @name references. Values must be JSON scalars.",
                            ["additionalProperties"] = new JsonObject
                            {
                                ["oneOf"] = Array(
                                    new JsonObject { ["type"] = "string" },
                                    new JsonObject { ["type"] = "number" },
                                    new JsonObject { ["type"] = "boolean" },
                                    new JsonObject { ["type"] = "null" }),
                            },
                        },
                    },
                },
                ["QueryErrorResponse"] = new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = Array(
                        JsonValue.Create("code"),
                        JsonValue.Create("message")),
                    ["properties"] = new JsonObject
                    {
                        ["code"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Stable error code, for example TYPE_MISMATCH.",
                        },
                        ["message"] = new JsonObject
                        {
                            ["type"] = "string",
                        },
                        ["position"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "0-based position in the decoded query, when known.",
                        },
                        ["length"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Length of the offending part, when known.",
                        },
                    },
                },
            },
        };

    private static JsonArray Array(params JsonNode?[] items) => new(items);
}
