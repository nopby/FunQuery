using FunQuery.Enums;
using FunQuery.Execution;
using FunQuery.SemanticTypes;

namespace FunQuery;

/// <summary>Hasil sebuah query: tipe semantik dan nilai runtime.</summary>
public readonly record struct QueryResult(SemanticType Type, object? Value)
{
    /// <summary>Nilai sebagai JSON. Hasil kosong menjadi <c>[]</c>.</summary>
    public string ToJson() => ValueJson.Serialize(Value);
}

/// <summary>
/// Pintu masuk untuk menjalankan query: lexer, parser, analyzer, lalu interpreter in-memory.
/// Memegang <see cref="QueryLimits"/> dan registry function sekali, sehingga tidak ada tahap
/// yang lupa menerima batas. Aman dipakai bersama antar-thread selama registry dan tabel
/// implementasi tidak diubah setelah engine dibuat.
/// </summary>
public sealed class QueryEngine
{
    private static readonly IReadOnlyDictionary<string, SemanticType> NoIdentifiers =
        new Dictionary<string, SemanticType>();

    private readonly FunctionRegistry _functions;
    private readonly InMemoryInterpreter _interpreter;

    public QueryEngine(
        QueryLimits? limits = null,
        FunctionRegistry? functions = null,
        InMemoryFunctions? implementations = null)
    {
        Limits = limits ?? QueryLimits.Default;

        _functions = (functions ?? new FunctionRegistry().AddExtension(new CoreFunctions()))
            .Freeze();

        _interpreter = new InMemoryInterpreter(implementations);
    }

    public QueryLimits Limits { get; }

    /// <summary>
    /// Menjalankan <paramref name="query"/>. Semua kesalahan input dilaporkan sebagai
    /// <see cref="QueryException"/> dengan kode dan posisi.
    /// </summary>
    public QueryResult Execute(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var buffer = new TokenBuffer(limits: Limits);
        Lexer.Tokenize(buffer, query);

        var parsed = Parser.Parse(query, buffer.Span, Limits);

        var context = new SemanticContext(query.AsMemory(), NoIdentifiers, _functions, Limits);
        var analyzed = SemanticAnalyzer.Analyze(parsed, context);

        var value = _interpreter.Execute(analyzed, query.AsMemory());

        return new QueryResult(
            analyzed.SemanticType
                ?? throw new QueryException(
                    QueryErrorCode.InternalError,
                    "The analyzed query has no semantic type."),
            value);
    }
}
