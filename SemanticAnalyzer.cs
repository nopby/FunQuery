using FunQuery.Enums;
using FunQuery.Expressions;
using FunQuery.SemanticTypes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FunQuery;

public static class SemanticAnalyzer
{
    public static BaseExpression Analyze(
        BaseExpression expression,
        SemanticContext context)
    {
        context.EnterNode(expression);

        var result = AnalyzeCore(expression, context);

        context.ExitNode();

        return result;
    }

    private static BaseExpression AnalyzeCore(
        BaseExpression expression,
        SemanticContext context)
    {
        return expression switch
        {
            ValueExpression value =>
                AnalyzeValue(value, context),

            IdentifierExpression identifier =>
                AnalyzeIdentifier(identifier, context),

            NamedExpression named =>
                AnalyzeNamed(named, context),

            ArrayExpression array =>
                AnalyzeArray(array, context),

            BlockExpression block =>
                AnalyzeBlock(block, context),

            ComparisonExpression comparison =>
                AnalyzeComparison(comparison, context),

            LogicalExpression logical =>
                AnalyzeLogical(logical, context),

            NotExpression negation =>
                AnalyzeNot(negation, context),

            VariableExpression variable =>
                AnalyzeVariable(variable, context),

            CallExpression call =>
                AnalyzeCall(call, context),

            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                $"Unsupported expression type: " +
                $"{expression.GetType().Name}.")
        };
    }

    private static ValueExpression AnalyzeValue(
        ValueExpression expression,
        SemanticContext context)
    {
        expression.SemanticType =
            GetValueType(expression, context);

        return expression;
    }

    private static IdentifierExpression AnalyzeIdentifier(
        IdentifierExpression expression,
        SemanticContext context)
    {
        var name =
            context.GetIdentifierName(expression);

        var type =
            context.ResolveIdentifier(name);

        if (type is null)
        {
            throw new QueryException(
                QueryErrorCode.UnknownIdentifier,
                $"Unknown identifier '{name}'.",
                expression.Token);
        }

        expression.SemanticType = type;

        return expression;
    }

    private static NamedExpression AnalyzeNamed(
        NamedExpression expression,
        SemanticContext context)
    {
        Analyze(
            expression.Value,
            context);

        expression.SemanticType =
            expression.Value.SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "Named expression has no semantic type.");

        return expression;
    }

    private static ArrayExpression AnalyzeArray(
        ArrayExpression expression,
        SemanticContext context)
    {
        foreach (var element in expression.Elements)
        {
            Analyze(element, context);
        }

        expression.SemanticType =
            context.ResolveArrayType(expression);

        return expression;
    }

    private static BlockExpression AnalyzeBlock(
        BlockExpression expression,
        SemanticContext context)
    {
        foreach (var child in expression.Expressions)
        {
            Analyze(child, context);
        }

        expression.SemanticType =
            context.ResolveBlockType(expression);

        return expression;
    }

    private static ComparisonExpression AnalyzeComparison(
        ComparisonExpression expression,
        SemanticContext context)
    {
        Analyze(
            expression.Left,
            context);

        Analyze(
            expression.Right,
            context);

        var leftType =
            expression.Left.SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "Left side of comparison has no semantic type.");

        var rightType =
            expression.Right.SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "Right side of comparison has no semantic type.");

        if (expression.Operator is ComparisonOperator.In)
        {
            ValidateIn(context, expression, leftType, rightType);
            expression.SemanticType = SemanticTypeOptions.Boolean;

            return expression;
        }

        if (expression.Operator is
            ComparisonOperator.Contains or
            ComparisonOperator.StartsWith or
            ComparisonOperator.EndsWith)
        {
            ValidateText(expression, leftType, rightType);
            expression.SemanticType = SemanticTypeOptions.Boolean;

            return expression;
        }

        if (!context.CanCompare(
                leftType,
                rightType))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Cannot compare " +
                $"{leftType.Name} with " +
                $"{rightType.Name}.",
                expression.Span);
        }

        ValidateComparisonOperator(expression, leftType, rightType);

        expression.SemanticType =
            SemanticTypeOptions.Boolean;

        return expression;
    }

    private static NotExpression AnalyzeNot(
        NotExpression expression,
        SemanticContext context)
    {
        Analyze(expression.Operand, context);

        var operandType =
            expression.Operand.SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "Operand of 'not' has no semantic type.");

        if (operandType is not BooleanType)
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operand of 'not' must be Boolean, got {operandType.Name}.",
                expression.Operand.Span);
        }

        expression.SemanticType = SemanticTypeOptions.Boolean;

        return expression;
    }

    /// <summary>
    /// in: nilai skalar di kiri, array di kanan. Elemen array harus bisa dibandingkan dengan nilai itu.
    /// Lihat docs/Operators.md.
    /// </summary>
    private static void ValidateIn(
        SemanticContext context,
        ComparisonExpression expression,
        SemanticType leftType,
        SemanticType rightType)
    {
        if (!SemanticContext.IsScalar(leftType))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator 'in' cannot be applied to {leftType.Name}.",
                expression.Left.Span);
        }

        if (rightType is AnyType)
            return;

        if (rightType is not ArrayType array)
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator 'in' needs an array on the right side, got {rightType.Name}.",
                expression.Right.Span);
        }

        // [] tidak punya tipe elemen: "x in []" selalu false dan selalu valid.
        if (array.Type is UnknownType)
            return;

        if (!SemanticContext.IsScalar(array.Type))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator 'in' cannot be applied to an array of {array.Type.Name}.",
                expression.Right.Span);
        }

        if (!context.CanCompare(leftType, array.Type))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Cannot compare {leftType.Name} with {array.Type.Name}.",
                expression.Span);
        }

        var hasNull = leftType is NullType || array.Type is NullType;

        if (!hasNull &&
            (SemanticContext.IsApproximate(leftType) ||
             SemanticContext.IsApproximate(array.Type)))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                "Operator 'in' is not supported for float and double, " +
                "because it tests equality.",
                expression.Span);
        }
    }

    /// <summary>contains, startswith, endswith: kedua operand harus string.</summary>
    private static void ValidateText(
        ComparisonExpression expression,
        SemanticType leftType,
        SemanticType rightType)
    {
        var keyword = ToKeyword(expression.Operator);

        if (leftType is not (StringType or NullType or AnyType))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator '{keyword}' requires string operands, got {leftType.Name}.",
                expression.Left.Span);
        }

        if (rightType is not (StringType or NullType or AnyType))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator '{keyword}' requires string operands, got {rightType.Name}.",
                expression.Right.Span);
        }
    }

    /// <summary>
    /// Aturan per operator, dipanggil setelah kedua sisi terbukti kompatibel.
    /// Lihat docs/Operators.md.
    /// </summary>
    private static void ValidateComparisonOperator(
        ComparisonExpression expression,
        SemanticType leftType,
        SemanticType rightType)
    {
        var keyword = ToKeyword(expression.Operator);

        // Array dan object tidak bisa dibandingkan dengan operator apa pun.
        if (!SemanticContext.IsScalar(leftType))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator '{keyword}' cannot be applied to {leftType.Name}.",
                expression.Left.Span);
        }

        if (!SemanticContext.IsScalar(rightType))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator '{keyword}' cannot be applied to {rightType.Name}.",
                expression.Right.Span);
        }

        var isEquality =
            expression.Operator is ComparisonOperator.Equal or ComparisonOperator.NotEqual;

        var hasNull = leftType is NullType || rightType is NullType;

        // null tidak punya urutan; hanya eq dan neq yang berlaku.
        if (!isEquality && hasNull)
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator '{keyword}' cannot be applied to null.",
                expression.Span);
        }

        // "price eq null" adalah uji null, bukan kesamaan nilai floating point, jadi diizinkan.
        if (isEquality &&
            !hasNull &&
            (SemanticContext.IsApproximate(leftType) ||
             SemanticContext.IsApproximate(rightType)))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator '{keyword}' is not supported for float and double. " +
                "Compare a range with gt, gte, lt or lte instead.",
                expression.Span);
        }

        // bool hanya punya kesamaan, tidak punya urutan.
        if (!isEquality && (leftType is BooleanType || rightType is BooleanType))
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                $"Operator '{keyword}' cannot be applied to bool.",
                expression.Span);
        }
    }

    private static string ToKeyword(ComparisonOperator op) =>
        op switch
        {
            ComparisonOperator.Equal => "eq",
            ComparisonOperator.NotEqual => "neq",
            ComparisonOperator.GreaterThan => "gt",
            ComparisonOperator.GreaterThanOrEqual => "gte",
            ComparisonOperator.LessThan => "lt",
            ComparisonOperator.LessThanOrEqual => "lte",
            ComparisonOperator.In => "in",
            ComparisonOperator.Contains => "contains",
            ComparisonOperator.StartsWith => "startswith",
            ComparisonOperator.EndsWith => "endswith",
            _ => op.ToString(),
        };

    private static LogicalExpression AnalyzeLogical(
        LogicalExpression expression,
        SemanticContext context)
    {
        Analyze(
            expression.Left,
            context);

        Analyze(
            expression.Right,
            context);

        var leftType =
            expression.Left.SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "Left side of logical expression has no semantic type.");

        var rightType =
            expression.Right.SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "Right side of logical expression has no semantic type.");

        if (leftType is not BooleanType)
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                "Left side of logical expression " +
                "must be Boolean.",
                expression.Left.Span);
        }

        if (rightType is not BooleanType)
        {
            throw new QueryException(
                QueryErrorCode.TypeMismatch,
                "Right side of logical expression " +
                "must be Boolean.",
                expression.Right.Span);
        }

        expression.SemanticType =
            SemanticTypeOptions.Boolean;

        return expression;
    }

    private static CallExpression AnalyzeCall(
    CallExpression expression,
    SemanticContext context)
    {
        // 1. Analyze target lebih dulu (kalau ada)
        if (expression.Target is not null)
            Analyze(expression.Target, context);

        // 2. Resolve fungsi
        var functionName = context.GetFunctionName(expression.Function);

        var function = context.ResolveFunction(functionName)
            ?? throw new QueryException(
                QueryErrorCode.UnknownFunction,
                $"Unknown function '{functionName}'.",
                expression.Function);

        // $let bukan function data biasa: ia tidak mengubah data (pass-through target),
        // dan tidak tunduk pada TargetRule/parameter generik, karena argumen pertamanya
        // adalah deklarasi (nama variable), bukan nilai yang dievaluasi. Dicocokkan lewat nama
        // (bukan identitas objek), sehingga tetap berfungsi bila $let didaftarkan oleh ekstensi lain.
        if (functionName == "$let")
            return AnalyzeLet(expression, context);

        // 3. Validasi target sebelum argumen, karena scope argumen bergantung pada tipe target
        ValidateTarget(expression, function, context);

        var targetType = expression.Target?.SemanticType;

        // 4. Analyze argumen (di dalam scope field elemen kalau fungsi membutuhkannya)
        if (function.UsesElementScope)
        {
            if (targetType is not ArrayType { Type: ObjectType element })
            {
                throw new QueryException(
                    QueryErrorCode.InvalidTarget,
                    $"Function '{function.Name}' requires an array of objects as target.",
                    expression.Function);
            }

            using (context.EnterScope(element.Fields))
            {
                foreach (var argument in expression.Arguments)
                    Analyze(argument, context);
            }
        }
        else
        {
            foreach (var argument in expression.Arguments)
                Analyze(argument, context);
        }

        // 5. Validasi jumlah dan tipe argumen
        ValidateArguments(expression, function);

        // 6. Hitung tipe hasil (overload dua parameter!)
        var argumentTypes = expression.Arguments
            .Select(a => a.SemanticType!)
            .ToList();

        expression.SemanticType = function.GetReturnType(targetType, argumentTypes);

        return expression;
    }

    /// <summary>
    /// $let(@nama, nilai): mengikat @nama ke tipe hasil `nilai` untuk step berikutnya dalam chain,
    /// tanpa mengubah data (pass-through dari target). Lihat docs/Variables.md.
    /// </summary>
    private static CallExpression AnalyzeLet(CallExpression expression, SemanticContext context)
    {
        if (expression.Arguments.Count != 2)
            throw new QueryException(
                QueryErrorCode.InvalidArgumentCount,
                "Function '$let' requires exactly 2 arguments: $let(@name, value).",
                expression.Function);

        if (expression.Arguments[0] is not VariableExpression variable)
            throw new QueryException(
                QueryErrorCode.InvalidLetTarget,
                "The first argument of '$let' must be a variable, e.g. $let(@name, value).",
                expression.Arguments[0].Span);

        // Nilai dianalisis dalam scope saat ini (mis. di dalam $filter bila $let dipakai di situ).
        // Snapshot lalu restore supaya $let bersarang di dalam nilai ini tidak bocor keluar;
        // hanya binding milik $let ini sendiri yang berlaku untuk step berikutnya.
        var snapshot = context.SnapshotVariables();

        Analyze(expression.Arguments[1], context);

        context.RestoreVariables(snapshot);

        var valueType =
            expression.Arguments[1].SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                "Value of '$let' has no semantic type.");

        var name = context.GetVariableName(variable);

        context.BindVariable(name, valueType, variable.Token);

        // Transparan terhadap data: hasil $let sama dengan targetnya (atau Unknown bila
        // $let berada di awal chain), sehingga function berikutnya melihat seolah $let
        // tidak ada dalam alur data.
        expression.SemanticType = expression.Target?.SemanticType ?? SemanticTypeOptions.Unknown;

        return expression;
    }

    private static VariableExpression AnalyzeVariable(
        VariableExpression expression,
        SemanticContext context)
    {
        var name = context.GetVariableName(expression);

        expression.SemanticType = context.ResolveVariable(name)
            ?? throw new QueryException(
                QueryErrorCode.UndefinedVariable,
                $"Variable '@{name}' is not defined.",
                expression.Token);

        return expression;
    }

    private static void ValidateArguments(CallExpression expression, FunctionDefinition function)
    {
        if (expression.Arguments.Count < function.MinArguments)
            throw new QueryException(
                QueryErrorCode.InvalidArgumentCount,
                $"Function '{function.Name}' requires at least {function.MinArguments} argument(s).",
                expression.Function);

        if (!function.IsVariadic && expression.Arguments.Count > function.Parameters.Count)
            throw new QueryException(
                QueryErrorCode.InvalidArgumentCount,
                $"Function '{function.Name}' accepts at most {function.Parameters.Count} argument(s).",
                expression.Function);

        if (function.Parameters.Count == 0)
            return;

        for (int i = 0; i < expression.Arguments.Count; i++)
        {
            var parameter = function.Parameters[Math.Min(i, function.Parameters.Count - 1)];

            var argumentType = expression.Arguments[i].SemanticType
                ?? throw new QueryException(
                    QueryErrorCode.InternalError,
                    $"Argument {i + 1} of '{function.Name}' has no semantic type.");

            if (!parameter.Accepts(argumentType))
                throw new QueryException(
                    QueryErrorCode.TypeMismatch,
                    $"Argument {i + 1} of '{function.Name}' expects {parameter.Expected}, got {argumentType.Name}.",
                    expression.Arguments[i].Span);
        }
    }

    private static void ValidateTarget(
        CallExpression expression,
        FunctionDefinition function,
        SemanticContext context)
    {
        if (expression.Target is null)
        {
            if (function.TargetRule == TargetRule.Required)
                throw new QueryException(
                    QueryErrorCode.InvalidTarget,
                    $"Function '{function.Name}' must be called on a target.",
                    expression.Function);
            return;
        }

        if (function.TargetRule == TargetRule.Forbidden)
        {
            // $let tidak menghitung sebagai target nyata: ia transparan terhadap data,
            // jadi "$let(...).$source(...)" tetap sah selama tidak ada data sungguhan
            // (call selain $let) di depannya.
            if (HasRealTarget(expression.Target, context))
                throw new QueryException(
                    QueryErrorCode.InvalidTarget,
                    $"Function '{function.Name}' must start the chain and cannot be called on a target.",
                    expression.Function);

            return;
        }

        var targetType = expression.Target.SemanticType
            ?? throw new QueryException(
                QueryErrorCode.InternalError,
                $"Target of '{function.Name}' has no semantic type.");

        if (!function.AcceptsTarget(targetType))
            throw new QueryException(
                QueryErrorCode.InvalidTarget,
                $"Function '{function.Name}' cannot be called on {targetType.Name}.",
                expression.Function);
    }

    /// <summary>True bila ada call selain $let di sepanjang rantai target.</summary>
    private static bool HasRealTarget(BaseExpression? target, SemanticContext context)
    {
        while (target is CallExpression call)
        {
            if (context.GetFunctionName(call.Function) != "$let")
                return true;

            target = call.Target;
        }

        return false;
    }

    private static SemanticType GetValueType(
        ValueExpression expression,
        SemanticContext context)
    {
        return expression.Token.Type switch
        {
            TokenType.StringLiteral =>
                SemanticTypeOptions.String,

            TokenType.Number => ResolveNumberType(expression, context),

            TokenType.BooleanLiteral =>
                SemanticTypeOptions.Boolean,

            TokenType.NullLiteral =>
                SemanticTypeOptions.Null,

            _ => throw new QueryException(
                QueryErrorCode.InternalError,
                $"Unsupported literal type: " +
                $"{expression.Token.Type}.")
        };
    }
    private static SemanticType ResolveNumberType(
    ValueExpression expression,
    SemanticContext context)
    {
        var text = context.GetText(expression.Token);

        if (text.Contains('.'))
        {
            if (decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out _))
                return SemanticTypeOptions.Decimal;

            throw new QueryException(
                QueryErrorCode.NumberOutOfRange,
                $"Number '{text}' is out of range.",
                expression.Token);
        }

        if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            return SemanticTypeOptions.Int;

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            return SemanticTypeOptions.Long;

        // Bilangan bulat di atas long masih muat sebagai decimal
        if (decimal.TryParse(text, NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out _))
            return SemanticTypeOptions.Decimal;

        throw new QueryException(
            QueryErrorCode.NumberOutOfRange,
            $"Number '{text}' is out of range.",
                expression.Token);
    }
}