using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
namespace FunQuery;

public sealed class SemanticContext
{
    private readonly ReadOnlyMemory<char> _source;

    private readonly IReadOnlyDictionary<string, SemanticType> _identifiers;

    private readonly IReadOnlyDictionary<string, FunctionDefinition> _functions;

    // Stack scope untuk identifier lokal (mis. field elemen di dalam $filter).
    // Enumerasi Stack<T> dimulai dari elemen paling atas (scope terdalam).
    private readonly Stack<IReadOnlyDictionary<string, SemanticType>> _scopes = new();

    private readonly int _maxDepth;
    private int _depth;

    public SemanticContext(
        ReadOnlyMemory<char> source,
        IReadOnlyDictionary<string, SemanticType> identifiers,
        IReadOnlyDictionary<string, FunctionDefinition> functions,
        QueryLimits? limits = null)
    {
        _source = source;
        _identifiers = identifiers;
        _functions = functions;
        _maxDepth = (limits ?? QueryLimits.Default).MaxDepth;
    }

    // ------------------------------------------------------------------
    // Kedalaman rekursi analyzer
    // ------------------------------------------------------------------

    public void EnterNode(BaseExpression expression)
    {
        if (++_depth > _maxDepth)
            throw new QueryException(
                QueryErrorCode.MaxDepthExceeded,
                $"Expression is nested deeper than {_maxDepth} levels.",
                expression.Span);
    }

    public void ExitNode() => _depth--;

    // ------------------------------------------------------------------
    // Source text
    // ------------------------------------------------------------------

    public string GetText(Token token) =>
        _source.Span[token.StartPosition..token.EndPosition].ToString();

    public string GetFunctionName(Token token) => GetText(token);

    public string GetIdentifierName(IdentifierExpression expression) =>
        GetText(expression.Token);

    // ASUMSI: NamedExpression punya properti `Token Name`.
    // Sesuaikan jika nama/tipe propertinya berbeda.
    public string GetNamedName(NamedExpression expression) =>
        GetText(expression.Name);

    // ------------------------------------------------------------------
    // Scope
    // ------------------------------------------------------------------

    public void PushScope(IReadOnlyDictionary<string, SemanticType> scope) =>
        _scopes.Push(scope);

    public void PopScope()
    {
        if (_scopes.Count == 0)
            throw new QueryException(
                QueryErrorCode.InternalError,
                "Cannot pop scope: no active scope.");

        _scopes.Pop();
    }

    /// <summary>
    /// Push scope dan kembalikan IDisposable yang otomatis pop.
    /// Pemakaian: using (context.EnterScope(fields)) { ... }
    /// </summary>
    public IDisposable EnterScope(IReadOnlyDictionary<string, SemanticType> scope)
    {
        PushScope(scope);
        return new ScopeGuard(this);
    }

    private sealed class ScopeGuard(SemanticContext context) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            context.PopScope();
        }
    }

    // ------------------------------------------------------------------
    // Resolve
    // ------------------------------------------------------------------

    public SemanticType? ResolveIdentifier(string name)
    {
        // Scope terdalam dulu, baru identifier global.
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name, out var scoped))
                return scoped;
        }

        return _identifiers.TryGetValue(name, out var type)
            ? type
            : null;
    }

    public FunctionDefinition? ResolveFunction(string name) =>
        _functions.TryGetValue(name, out var function)
            ? function
            : null;

    public SemanticType ResolveArrayType(ArrayExpression expression)
    {
        if (expression.Elements.Count == 0)
            return new ArrayType(SemanticTypeOptions.Unknown);

        var firstType =
            expression.Elements[0].SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "Array element has no semantic type.");

        for (int i = 1; i < expression.Elements.Count; i++)
        {
            var elementType =
                expression.Elements[i].SemanticType
                ?? throw new QueryException(
                    QueryErrorCode.InternalError,
                    $"Array element at index {i} has no semantic type.");

            if (!AreCompatible(firstType, elementType))
            {
                var message = firstType is ObjectType && elementType is ObjectType
                    ? $"Array element at index {i} has different fields than the first element."
                    : $"Array elements must have compatible types. " +
                      $"Expected {firstType.Name}, " +
                      $"got {elementType.Name} at index {i}.";

                throw new QueryException(
                    QueryErrorCode.IncompatibleElementTypes,
                    message,
                    expression.Elements[i].Span);
            }
        }

        return new ArrayType(firstType);
    }

    public SemanticType ResolveBlockType(BlockExpression expression)
    {
        if (expression.Expressions.Count == 0)
            return SemanticTypeOptions.Void;

        // Object literal: semua child adalah NamedExpression, mis. {id: 1, name: 'x'}
        if (expression.Expressions.All(e => e is NamedExpression))
            return ResolveObjectType(expression);

        // Block biasa: tipe = tipe ekspresi terakhir.
        return expression.Expressions[^1].SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "Last expression in block has no semantic type.");
    }

    private ObjectType ResolveObjectType(BlockExpression expression)
    {
        var fields = new Dictionary<string, SemanticType>();

        foreach (var child in expression.Expressions)
        {
            var named = (NamedExpression)child;
            var name = GetNamedName(named);

            if (fields.ContainsKey(name))
                throw new QueryException(
                    QueryErrorCode.DuplicateField,
                    $"Duplicate field '{name}' in object.",
                    named.Name);

            fields[name] = named.SemanticType
                ?? throw new QueryException(
                    QueryErrorCode.InternalError,
                    $"Field '{name}' has no semantic type.");
        }

        return new ObjectType(fields);
    }

    // ------------------------------------------------------------------
    // Type rules
    // ------------------------------------------------------------------

    public bool CanCompare(SemanticType left, SemanticType right)
    {
        if (left == SemanticTypeOptions.Unknown ||
            right == SemanticTypeOptions.Unknown)
        {
            return false;
        }

        if (AreCompatible(left, right))
            return true;

        return IsNumeric(left) && IsNumeric(right);
    }

    public static bool AreCompatible(SemanticType left, SemanticType right)
    {
        if (left == right)
            return true;

        if (left is AnyType || right is AnyType)
            return true;

        if (left is ArrayType la && right is ArrayType ra)
        {
            // Array kosong (element Unknown) kompatibel dengan array apa pun.
            if (la.Type is UnknownType || ra.Type is UnknownType)
                return true;

            return AreCompatible(la.Type, ra.Type);
        }

        // ObjectType berisi dictionary sehingga equality bawaan record
        // membandingkan referensi. Bandingkan secara struktural.
        if (left is ObjectType lo && right is ObjectType ro)
        {
            return lo.Fields.Count == ro.Fields.Count &&
                   lo.Fields.All(f =>
                       ro.Fields.TryGetValue(f.Key, out var other) &&
                       AreCompatible(f.Value, other));
        }

        return false;
    }

    public static bool IsNumeric(SemanticType type) =>
        type switch
        {
            IntType => true,
            LongType => true,
            FloatType => true,
            DoubleType => true,
            DecimalType => true,
            _ => false
        };
}