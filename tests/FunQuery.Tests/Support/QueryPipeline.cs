using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;

namespace FunQuery.Tests.Support;

/// <summary>
/// Runs the same steps as Program.cs (lex, parse, analyze) entirely in memory.
/// No database is involved anywhere in this test project.
/// </summary>
internal static class QueryPipeline
{
    private static readonly IReadOnlyDictionary<string, SemanticType> NoIdentifiers =
        new Dictionary<string, SemanticType>();

    /// <summary>A fresh registry containing only the core functions.</summary>
    public static FunctionRegistry CoreRegistry() =>
        new FunctionRegistry().AddExtension(new CoreFunctions());

    public static IReadOnlyDictionary<string, SemanticType> Identifiers(
        params (string Name, SemanticType Type)[] items) =>
        items.ToDictionary(item => item.Name, item => item.Type);

    public static Token[] Tokenize(string input, QueryLimits? limits = null)
    {
        using var buffer = new TokenBuffer(limits: limits);
        Lexer.Tokenize(buffer, input);
        return buffer.Span.ToArray();
    }

    public static BaseExpression Parse(string input, QueryLimits? limits = null)
    {
        using var buffer = new TokenBuffer(limits: limits);
        Lexer.Tokenize(buffer, input);
        return Parser.Parse(input, buffer.Span, limits);
    }

    public static BaseExpression Analyze(
        string input,
        IReadOnlyDictionary<string, SemanticType>? identifiers = null,
        QueryLimits? limits = null,
        FunctionRegistry? functions = null)
    {
        var parsed = Parse(input, limits);

        var context = new SemanticContext(
            input.AsMemory(),
            identifiers ?? NoIdentifiers,
            functions ?? CoreRegistry(),
            limits);

        return SemanticAnalyzer.Analyze(parsed, context);
    }
}
