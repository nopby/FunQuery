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
    /// <summary>A fresh registry containing only the core functions.</summary>
    public static FunctionRegistry CoreRegistry() =>
        new FunctionRegistry().AddExtension(new CoreFunctions());

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
        QueryLimits? limits = null,
        FunctionRegistry? functions = null,
        IReadOnlyDictionary<string, SemanticType>? variables = null)
    {
        var parsed = Parse(input, limits);

        var context = new SemanticContext(
            input.AsMemory(),
            functions ?? CoreRegistry(),
            limits,
            variables);

        return SemanticAnalyzer.Analyze(parsed, context);
    }
}
