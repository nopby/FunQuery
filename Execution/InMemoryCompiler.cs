using System.Globalization;
using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;

namespace FunQuery.Execution;

/// <summary>
/// Mengubah AST yang sudah dianalisis menjadi susunan delegate ("closure compilation").
/// Tidak memakai reflection maupun Expression Trees, sehingga aman untuk Native AOT.
/// Setiap node dikompilasi sekali; delegate hasilnya dipanggil per elemen.
/// </summary>
/// <remarks>
/// Rekursi dibatasi oleh <see cref="QueryLimits.MaxDepth"/>, karena analyzer sudah menolak pohon
/// yang lebih dalam sebelum kompilasi dimulai.
/// </remarks>
public sealed class InMemoryCompiler
{
    private static readonly object True = true;
    private static readonly object False = false;

    private readonly ReadOnlyMemory<char> _source;
    private readonly InMemoryFunctions _functions;

    // Nilai variable saat kompilasi. Dictionary datar, sama seperti tipe variable di analyzer;
    // bertambah lewat $let, dan snapshot/restore menjaga scope tetap lexical ke depan.
    private Dictionary<string, object?> _variables;

    internal InMemoryCompiler(
        ReadOnlyMemory<char> source,
        InMemoryFunctions functions,
        IReadOnlyDictionary<string, object?>? variables = null)
    {
        _source = source;
        _functions = functions;
        _variables = variables is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(variables, StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------
    // Variable (@nama)
    // ------------------------------------------------------------------

    internal string GetVariableName(VariableExpression expression) =>
        TextOf(expression.Token)[1..];

    internal object? ResolveVariable(string name) =>
        _variables.TryGetValue(name, out var value)
            ? value
            // Seharusnya tidak pernah terjadi: analyzer sudah menolak variable yang tidak
            // terdefinisi (UNDEFINED_VARIABLE) sebelum kompilasi dimulai.
            : throw new QueryException(
                QueryErrorCode.InternalError,
                $"Variable '@{name}' has no runtime value. Was the query analyzed?");

    internal void BindVariable(string name, object? value) => _variables[name] = value;

    internal IReadOnlyDictionary<string, object?> SnapshotVariables() =>
        new Dictionary<string, object?>(_variables, StringComparer.Ordinal);

    internal void RestoreVariables(IReadOnlyDictionary<string, object?> snapshot) =>
        _variables = new Dictionary<string, object?>(snapshot, StringComparer.Ordinal);

    /// <summary>
    /// Mengkompilasi sebuah ekspresi menjadi delegate yang menerima elemen saat ini
    /// (null di luar konteks per-elemen) dan menghasilkan nilainya.
    /// </summary>
    public Func<object?, object?> Compile(BaseExpression expression) =>
        expression switch
        {
            ValueExpression value => CompileValue(value),
            IdentifierExpression identifier => CompileField(identifier),
            VariableExpression variable => CompileVariable(variable),
            ArrayExpression array => CompileArray(array),
            BlockExpression block => CompileObject(block),
            ComparisonExpression comparison => CompileComparison(comparison),
            LogicalExpression logical => CompileLogical(logical),
            NotExpression negation => CompileNot(negation),
            CallExpression call => CompileCall(call),
            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                $"Cannot execute expression of type {expression.GetType().Name}.",
                expression.Span),
        };

    private string TextOf(Token token) =>
        _source.Span[token.StartPosition..token.EndPosition].ToString();

    private Func<object?, object?> CompileValue(ValueExpression expression)
    {
        object? constant;

        switch (expression.Token.Type)
        {
            case TokenType.StringLiteral:
                constant = StringLiteral.Unquote(
                    _source.Span[expression.Token.StartPosition..expression.Token.EndPosition]);
                break;

            case TokenType.BooleanLiteral:
                constant = TextOf(expression.Token) == "true" ? True : False;
                break;

            case TokenType.NullLiteral:
                constant = null;
                break;

            case TokenType.Number:
                constant = ParseNumber(expression);
                break;

            default:
                throw new QueryException(
                    QueryErrorCode.InternalError,
                    $"Unsupported literal type: {expression.Token.Type}.",
                    expression.Span);
        }

        return _ => constant;
    }

    private object ParseNumber(ValueExpression expression)
    {
        var text = TextOf(expression.Token);

        // Setiap arm di-box ke object. Tanpa itu compiler memilih decimal sebagai tipe bersama
        // dan int atau long diam-diam berubah menjadi decimal.
        return expression.SemanticType switch
        {
            IntType => (object)int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            LongType => (object)long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            DecimalType => (object)decimal.Parse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                "Number literal has no numeric semantic type. Was the query analyzed?",
                expression.Span),
        };
    }

    // Variable dibaca dari _variables saat delegate dipanggil, bukan saat dikompilasi:
    // sebuah $filter yang dikompilasi sebelum $let-nya sendiri dieksekusi (belum terjadi
    // di v1 karena urutan kompilasi mengikuti urutan chain) tetap membaca nilai yang benar.
    private Func<object?, object?> CompileVariable(VariableExpression expression)
    {
        var name = GetVariableName(expression);

        return _ => ResolveVariable(name);
    }

    private Func<object?, object?> CompileField(IdentifierExpression expression)
    {
        var name = TextOf(expression.Token);

        // Field yang tidak ada dibaca sebagai null.
        return element =>
            element is ObjectValue row && row.TryGetValue(name, out var value)
                ? value
                : null;
    }

    private Func<object?, object?> CompileArray(ArrayExpression expression)
    {
        var elements = expression.Elements.Select(Compile).ToArray();

        return element =>
        {
            var list = new List<object?>(elements.Length);

            foreach (var compiled in elements)
                list.Add(compiled(element));

            return list;
        };
    }

    private Func<object?, object?> CompileObject(BlockExpression expression)
    {
        var keys = new string[expression.Expressions.Count];
        var values = new Func<object?, object?>[keys.Length];

        for (int i = 0; i < keys.Length; i++)
        {
            if (expression.Expressions[i] is not NamedExpression named)
            {
                throw new QueryException(
                    QueryErrorCode.InternalError,
                    "Object literal contains an entry that is not 'name: value'.",
                    expression.Expressions[i].Span);
            }

            keys[i] = named.Name.Type == TokenType.StringLiteral
                ? StringLiteral.Unquote(_source.Span[named.Name.StartPosition..named.Name.EndPosition])
                : TextOf(named.Name);
            values[i] = Compile(named.Value);
        }

        var shape = new ObjectShape(keys);

        return element =>
        {
            var row = new object?[values.Length];

            for (int i = 0; i < row.Length; i++)
                row[i] = values[i](element);

            return new ObjectValue(shape, row);
        };
    }

    private Func<object?, object?> CompileComparison(ComparisonExpression expression)
    {
        var left = Compile(expression.Left);
        var right = Compile(expression.Right);

        return expression.Operator switch
        {
            ComparisonOperator.Equal =>
                element => ValueOperations.Equal(left(element), right(element)) ? True : False,

            ComparisonOperator.NotEqual =>
                element => ValueOperations.Equal(left(element), right(element)) ? False : True,

            ComparisonOperator.GreaterThan =>
                Ordered(left, right, static c => c > 0),

            ComparisonOperator.GreaterThanOrEqual =>
                Ordered(left, right, static c => c >= 0),

            ComparisonOperator.LessThan =>
                Ordered(left, right, static c => c < 0),

            ComparisonOperator.LessThanOrEqual =>
                Ordered(left, right, static c => c <= 0),

            ComparisonOperator.In =>
                element => ValueOperations.In(left(element), right(element)) ? True : False,

            ComparisonOperator.Contains =>
                element => ValueOperations.Contains(left(element), right(element)) ? True : False,

            ComparisonOperator.StartsWith =>
                element => ValueOperations.StartsWith(left(element), right(element)) ? True : False,

            ComparisonOperator.EndsWith =>
                element => ValueOperations.EndsWith(left(element), right(element)) ? True : False,

            _ => throw new QueryException(
                QueryErrorCode.NotSupported,
                $"Operator '{expression.Operator}' is not supported by the in-memory provider.",
                expression.Span),
        };
    }

    // Urutan dengan null (atau nilai yang tidak berurutan) bernilai false.
    private static Func<object?, object?> Ordered(
        Func<object?, object?> left,
        Func<object?, object?> right,
        Func<int, bool> test) =>
        element =>
            ValueOperations.TryCompare(left(element), right(element), out var result) && test(result)
                ? True
                : False;

    private Func<object?, object?> CompileLogical(LogicalExpression expression)
    {
        var left = Compile(expression.Left);
        var right = Compile(expression.Right);

        return expression.Operator switch
        {
            LogicalOperator.And =>
                element => ValueOperations.IsTrue(left(element)) && ValueOperations.IsTrue(right(element))
                    ? True
                    : False,

            LogicalOperator.Or =>
                element => ValueOperations.IsTrue(left(element)) || ValueOperations.IsTrue(right(element))
                    ? True
                    : False,

            _ => throw new QueryException(
                QueryErrorCode.NotSupported,
                $"Operator '{expression.Operator}' is not supported by the in-memory provider.",
                expression.Span),
        };
    }

    // null dihitung false, jadi not null bernilai true.
    private Func<object?, object?> CompileNot(NotExpression expression)
    {
        var operand = Compile(expression.Operand);

        return element => ValueOperations.IsTrue(operand(element)) ? False : True;
    }

    private Func<object?, object?> CompileCall(CallExpression call)
    {
        var name = TextOf(call.Function);

        if (!_functions.TryGet(name, out var implementation))
        {
            throw new QueryException(
                QueryErrorCode.NotSupported,
                $"Function '{name}' has no in-memory implementation.",
                call.Function);
        }

        return implementation(this, call);
    }
}
