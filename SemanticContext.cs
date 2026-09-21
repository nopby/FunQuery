using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
namespace FunQuery;

public sealed class SemanticContext
{
    private readonly ReadOnlyMemory<char> _source;

    private readonly IReadOnlyDictionary<string, SemanticType> _identifiers;

    private readonly FunctionRegistry _functions;

    // Stack scope untuk identifier lokal (mis. field elemen di dalam $filter).
    // Enumerasi Stack<T> dimulai dari elemen paling atas (scope terdalam).
    private readonly Stack<IReadOnlyDictionary<string, SemanticType>> _scopes = new();

    private readonly int _maxDepth;
    private int _depth;

    public SemanticContext(
        ReadOnlyMemory<char> source,
        IReadOnlyDictionary<string, SemanticType> identifiers,
        FunctionRegistry functions,
        QueryLimits? limits = null)
    {
        _source = source;
        _identifiers = identifiers;
        _functions = functions.Freeze();
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
        _functions.TryGet(name, out var function)
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

        // Tipe elemen digabung selama pemeriksaan: null menyatu dengan tipe apa pun,
        // jadi [null, 'a'] adalah array string.
        var elementTypeSoFar = firstType;

        for (int i = 1; i < expression.Elements.Count; i++)
        {
            var elementType =
                expression.Elements[i].SemanticType
                ?? throw new QueryException(
                    QueryErrorCode.InternalError,
                    $"Array element at index {i} has no semantic type.");

            var unified = Unify(elementTypeSoFar, elementType);

            if (unified is null)
            {
                var message = elementTypeSoFar is ObjectType && elementType is ObjectType
                    ? $"Array element at index {i} has different fields than the first element."
                    : $"Array elements must have compatible types. " +
                      $"Expected {elementTypeSoFar.Name}, " +
                      $"got {elementType.Name} at index {i}.";

                throw new QueryException(
                    QueryErrorCode.IncompatibleElementTypes,
                    message,
                    expression.Elements[i].Span);
            }

            elementTypeSoFar = unified;
        }

        return new ArrayType(elementTypeSoFar);
    }

    public SemanticType ResolveBlockType(BlockExpression expression)
    {
        // Parser menjamin setiap child adalah NamedExpression, mis. {id: 1, name: 'x'}.
        // Block kosong {} adalah object tanpa field.
        return ResolveObjectType(expression);
    }

    private ObjectType ResolveObjectType(BlockExpression expression)
    {
        var fields = new Dictionary<string, SemanticType>();

        foreach (var child in expression.Expressions)
        {
            if (child is not NamedExpression named)
                throw new QueryException(
                    QueryErrorCode.InternalError,
                    "Object literal contains an entry that is not 'name: value'.",
                    child.Span);

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

    public static bool AreCompatible(SemanticType left, SemanticType right) =>
        Unify(left, right) is not null;

    /// <summary>
    /// Menggabungkan dua tipe menjadi satu tipe yang mewakili keduanya, atau null bila tidak bisa.
    /// null menyatu dengan tipe apa pun (hasilnya tipe yang lain), array kosong menyatu dengan array apa pun,
    /// dan object menyatu bila field-nya sama dan setiap tipe field bisa digabung.
    /// </summary>
    public static SemanticType? Unify(SemanticType left, SemanticType right)
    {
        if (left == right)
            return left;

        if (left is AnyType)
            return left;

        if (right is AnyType)
            return right;

        if (left is NullType)
            return right;

        if (right is NullType)
            return left;

        if (left is ArrayType la && right is ArrayType ra)
        {
            // Array kosong (element Unknown) menyatu dengan array apa pun.
            if (la.Type is UnknownType)
                return ra;

            if (ra.Type is UnknownType)
                return la;

            var element = Unify(la.Type, ra.Type);

            return element is null ? null : new ArrayType(element);
        }

        // ObjectType berisi dictionary sehingga equality bawaan record
        // membandingkan referensi. Gabungkan secara struktural.
        if (left is ObjectType lo && right is ObjectType ro)
        {
            if (lo.Fields.Count != ro.Fields.Count)
                return null;

            var fields = new Dictionary<string, SemanticType>(lo.Fields.Count);

            foreach (var field in lo.Fields)
            {
                if (!ro.Fields.TryGetValue(field.Key, out var other))
                    return null;

                var merged = Unify(field.Value, other);

                if (merged is null)
                    return null;

                fields[field.Key] = merged;
            }

            return new ObjectType(fields);
        }

        return null;
    }

    /// <summary>
    /// Nilai tunggal yang boleh dibandingkan: angka, string, dan bool.
    /// AnyType (tipe belum diketahui saat analisis) diizinkan; array dan object tidak.
    /// </summary>
    public static bool IsScalar(SemanticType type) =>
        type is AnyType or NullType or StringType or BooleanType || IsNumeric(type);

    /// <summary>
    /// float dan double tidak menyimpan nilai desimal secara tepat, sehingga eq dan neq
    /// pada keduanya ditolak (lihat docs/Operators.md).
    /// </summary>
    public static bool IsApproximate(SemanticType type) =>
        type is FloatType or DoubleType;

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