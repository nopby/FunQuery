using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
namespace FunQuery;

public sealed class SemanticContext
{
    private readonly ReadOnlyMemory<char> _source;

    private readonly FunctionRegistry _functions;

    // Variable (@nama): dictionary datar, bertambah lewat $let. Tidak ada mekanisme
    // identifier global lagi; field hanya ada di scope elemen (lihat ResolveIdentifier).
    private Dictionary<string, SemanticType> _variables;

    // Stack scope untuk identifier lokal (mis. field elemen di dalam $filter).
    // Enumerasi Stack<T> dimulai dari elemen paling atas (scope terdalam).
    private readonly Stack<IReadOnlyDictionary<string, SemanticType>> _scopes = new();

    // Tipe elemen saat ini untuk '~', selalu didorong/dilepas bersama _scopes.
    private readonly Stack<SemanticType> _elementTypes = new();

    private static readonly IReadOnlyDictionary<string, SemanticType> NoFields =
        new Dictionary<string, SemanticType>();

    private readonly int _maxDepth;
    private int _depth;

    public SemanticContext(
        ReadOnlyMemory<char> source,
        FunctionRegistry functions,
        QueryLimits? limits = null,
        IReadOnlyDictionary<string, SemanticType>? variables = null)
    {
        _source = source;
        _functions = functions.Freeze();
        _maxDepth = (limits ?? QueryLimits.Default).MaxDepth;
        _variables = variables is null
            ? new Dictionary<string, SemanticType>(StringComparer.Ordinal)
            : new Dictionary<string, SemanticType>(variables, StringComparer.Ordinal);
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
        expression.Name.Type == TokenType.StringLiteral
            ? StringLiteral.Unquote(_source.Span[expression.Name.StartPosition..expression.Name.EndPosition])
            : GetText(expression.Name);

    /// <summary>Nama sebuah variable tanpa prefix '@'.</summary>
    public string GetVariableName(VariableExpression expression) =>
        GetText(expression.Token)[1..];

    public string GetFieldAccessName(FieldAccessExpression expression) =>
        GetText(expression.Field);

    /// <summary>Isi sebuah string literal (tanpa tanda kutip, '' sudah di-unescape).</summary>
    public string GetStringLiteralValue(ValueExpression expression) =>
        StringLiteral.Unquote(_source.Span[expression.Token.StartPosition..expression.Token.EndPosition]);

    // ------------------------------------------------------------------
    // Variable (@nama)
    // ------------------------------------------------------------------

    public SemanticType? ResolveVariable(string name) =>
        _variables.TryGetValue(name, out var type) ? type : null;

    /// <summary>
    /// Mengikat variable baru. declarationToken dipakai untuk posisi error bila nama
    /// sudah terikat sebelumnya (dari $let lain atau dari luar query).
    /// </summary>
    public void BindVariable(string name, SemanticType type, Token declarationToken)
    {
        if (_variables.ContainsKey(name))
            throw new QueryException(
                QueryErrorCode.VariableRedefined,
                $"Variable '@{name}' is already defined.",
                declarationToken);

        _variables[name] = type;
    }

    /// <summary>
    /// Snapshot variable saat ini. Dipakai $let untuk membuang binding yang dibuat oleh
    /// $let lain di dalam ekspresi nilainya sendiri, supaya scope tetap lexical ke depan.
    /// </summary>
    public IReadOnlyDictionary<string, SemanticType> SnapshotVariables() =>
        new Dictionary<string, SemanticType>(_variables, StringComparer.Ordinal);

    public void RestoreVariables(IReadOnlyDictionary<string, SemanticType> snapshot) =>
        _variables = new Dictionary<string, SemanticType>(snapshot, StringComparer.Ordinal);

    // ------------------------------------------------------------------
    // Scope
    // ------------------------------------------------------------------

    /// <summary>
    /// Masuk ke scope elemen: elementType menjadi tipe '~', dan field-nya (bila elementType
    /// berupa ObjectType) menjadi identifier polos yang bisa diakses. Elemen skalar (mis. int)
    /// tidak punya field, tapi '~' tetap bisa dipakai untuk merujuk nilainya langsung.
    /// Pemakaian: using (context.EnterScope(elementType)) { ... }
    /// </summary>
    public IDisposable EnterScope(SemanticType elementType)
    {
        var fields = elementType is ObjectType obj ? obj.Fields : NoFields;

        _elementTypes.Push(elementType);
        _scopes.Push(fields);

        return new ScopeGuard(this);
    }

    private void PopScope()
    {
        if (_scopes.Count == 0)
            throw new QueryException(
                QueryErrorCode.InternalError,
                "Cannot pop scope: no active scope.");

        _scopes.Pop();
        _elementTypes.Pop();
    }

    /// <summary>Tipe '~' pada scope elemen terdalam, atau null bila di luar konteks per-elemen.</summary>
    public SemanticType? CurrentElementType =>
        _elementTypes.Count > 0 ? _elementTypes.Peek() : null;

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

    /// <summary>
    /// Field dari item saat ini. Hanya ada di scope elemen (mis. di dalam $filter),
    /// tidak ada lagi mekanisme identifier global.
    /// </summary>
    public SemanticType? ResolveIdentifier(string name)
    {
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name, out var scoped))
                return scoped;
        }

        return null;
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
                // Field yang hanya ada di sebagian elemen tidak lagi jadi alasan gagal (lihat
                // Unify), jadi kegagalan di sini selalu berarti ada field yang sama namanya
                // tapi tipenya benar-benar tidak sejalan (atau tipe elemen yang sama sekali
                // beda keluarga, mis. int dan string).
                throw new QueryException(
                    QueryErrorCode.IncompatibleElementTypes,
                    $"Array elements must have compatible types. " +
                    $"Expected {elementTypeSoFar.Name}, " +
                    $"got {elementType.Name} at index {i}.",
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

        var widened = WidenNumeric(left, right);

        if (widened is not null)
            return widened;

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
        // membandingkan referensi. Gabungkan secara struktural, sebagai UNION field, bukan
        // irisan: field yang hanya ada di salah satu sisi tetap ikut (dibaca null pada baris
        // yang tidak memilikinya, lihat docs/DataTypes.md#objects). Hanya field yang ADA di
        // kedua sisi dan tipenya benar-benar tidak sejalan yang membuat unify gagal.
        if (left is ObjectType lo && right is ObjectType ro)
        {
            var fields = new Dictionary<string, SemanticType>(
                Math.Max(lo.Fields.Count, ro.Fields.Count),
                StringComparer.Ordinal);

            foreach (var key in lo.Fields.Keys.Union(ro.Fields.Keys, StringComparer.Ordinal))
            {
                var hasLeft = lo.Fields.TryGetValue(key, out var leftFieldType);
                var hasRight = ro.Fields.TryGetValue(key, out var rightFieldType);

                SemanticType? merged = hasLeft && hasRight
                    ? Unify(leftFieldType!, rightFieldType!)
                    : hasLeft ? leftFieldType : rightFieldType;

                if (merged is null)
                    return null;

                fields[key] = merged;
            }

            return new ObjectType(fields);
        }

        return null;
    }

    /// <summary>
    /// Melebarkan dua tipe angka ke tipe yang lebih luas (int &lt; long &lt; decimal, dan
    /// terpisah float &lt; double), atau null bila keduanya bukan pasangan angka yang sama
    /// keluarganya (exact atau approximate). Lihat docs/DataTypes.md#numbers.
    /// </summary>
    private static SemanticType? WidenNumeric(SemanticType left, SemanticType right)
    {
        var exactLeft = Array.IndexOf(ExactNumericOrder, left);
        var exactRight = Array.IndexOf(ExactNumericOrder, right);

        if (exactLeft >= 0 && exactRight >= 0)
            return ExactNumericOrder[Math.Max(exactLeft, exactRight)];

        var approximateLeft = Array.IndexOf(ApproximateNumericOrder, left);
        var approximateRight = Array.IndexOf(ApproximateNumericOrder, right);

        if (approximateLeft >= 0 && approximateRight >= 0)
            return ApproximateNumericOrder[Math.Max(approximateLeft, approximateRight)];

        return null;
    }

    private static readonly SemanticType[] ExactNumericOrder =
    [
        SemanticTypeOptions.Int,
        SemanticTypeOptions.Long,
        SemanticTypeOptions.Decimal,
    ];

    private static readonly SemanticType[] ApproximateNumericOrder =
    [
        SemanticTypeOptions.Float,
        SemanticTypeOptions.Double,
    ];

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