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

        _interpreter = new InMemoryInterpreter(implementations, Limits);
    }

    public QueryLimits Limits { get; }

    /// <summary>
    /// Menjalankan <paramref name="query"/> dengan parameter dari <paramref name="environment"/>.
    /// Semua kesalahan input dilaporkan sebagai <see cref="QueryException"/> dengan kode dan posisi.
    /// </summary>
    public QueryResult Execute(
        string query,
        QueryEnvironment? environment = null,
        CancellationToken cancellationToken = default) =>
        ExecutePlan(Prepare(query, environment), environment, cancellationToken);

    /// <summary>
    /// Melakukan lex, parse, dan analisis tanpa mengeksekusi query. Tipe parameter diambil dari
    /// <paramref name="environment"/>; nilainya tidak dipakai di tahap ini.
    /// </summary>
    public QueryPlan Prepare(string query, QueryEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        environment ??= QueryEnvironment.Empty;

        using var buffer = new TokenBuffer(limits: Limits);
        Lexer.Tokenize(buffer, query);

        var parsed = Parser.Parse(query, buffer.Span, Limits);

        var types = new Dictionary<string, SemanticType>(
            environment.Parameters.Count,
            StringComparer.Ordinal);

        foreach (var (name, parameter) in environment.Parameters)
            types[name] = parameter.Type;

        var context = new SemanticContext(query.AsMemory(), _functions, Limits, types);
        var analyzed = SemanticAnalyzer.Analyze(parsed, context);

        return new QueryPlan(query, analyzed, Limits);
    }

    /// <summary>
    /// Menjalankan query yang sudah disiapkan. <paramref name="environment"/> harus berisi tipe
    /// yang sama dengan yang dipakai saat <see cref="Prepare"/>; hanya nilainya yang dibaca di sini.
    /// </summary>
    public QueryResult ExecutePlan(
        QueryPlan plan,
        QueryEnvironment? environment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        environment ??= QueryEnvironment.Empty;

        var values = new Dictionary<string, object?>(
            environment.Parameters.Count,
            StringComparer.Ordinal);

        foreach (var (name, parameter) in environment.Parameters)
            values[name] = parameter.Value;

        var value = _interpreter.Execute(
            plan.Expression,
            plan.Query.AsMemory(),
            values,
            cancellationToken);

        return new QueryResult(
            plan.Expression.SemanticType
                ?? throw new QueryException(
                    QueryErrorCode.InternalError,
                    "The analyzed query has no semantic type."),
            value);
    }
}
