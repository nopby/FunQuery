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

            CallExpression call =>
                AnalyzeCall(call, context),

            _ => throw new SemanticException(
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
            throw new SemanticException(
                $"Unknown identifier '{name}'.");
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
            ?? throw new SemanticException(
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
            ?? throw new SemanticException(
                "Left side of comparison has no semantic type.");

        var rightType =
            expression.Right.SemanticType
            ?? throw new SemanticException(
                "Right side of comparison has no semantic type.");

        if (!context.CanCompare(
                leftType,
                rightType))
        {
            throw new SemanticException(
                $"Cannot compare " +
                $"{leftType.Name} with " +
                $"{rightType.Name}.");
        }

        expression.SemanticType =
            SemanticTypeOptions.Boolean;

        return expression;
    }

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
            ?? throw new SemanticException(
                "Left side of logical expression has no semantic type.");

        var rightType =
            expression.Right.SemanticType
            ?? throw new SemanticException(
                "Right side of logical expression has no semantic type.");

        if (leftType is not BooleanType)
        {
            throw new SemanticException(
                "Left side of logical expression " +
                "must be Boolean.");
        }

        if (rightType is not BooleanType)
        {
            throw new SemanticException(
                "Right side of logical expression " +
                "must be Boolean.");
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
            ?? throw new SemanticException($"Unknown function '{functionName}'.");

        // 3. Validasi target sebelum argumen, karena scope argumen bergantung pada tipe target
        ValidateTarget(expression, function);

        var targetType = expression.Target?.SemanticType;

        // 4. Analyze argumen (di dalam scope field elemen kalau fungsi membutuhkannya)
        if (function.UsesElementScope)
        {
            if (targetType is not ArrayType { Type: ObjectType element })
            {
                throw new SemanticException(
                    $"Function '{function.Name}' requires an array of objects as target.");
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

    private static void ValidateArguments(CallExpression expression, FunctionDefinition function)
    {
        if (expression.Arguments.Count < function.MinArguments)
            throw new SemanticException(
                $"Function '{function.Name}' requires at least {function.MinArguments} argument(s).");

        if (!function.IsVariadic && expression.Arguments.Count > function.Parameters.Count)
            throw new SemanticException(
                $"Function '{function.Name}' accepts at most {function.Parameters.Count} argument(s).");

        for (int i = 0; i < expression.Arguments.Count; i++)
        {
            var parameter = function.Parameters[Math.Min(i, function.Parameters.Count - 1)];

            var argumentType = expression.Arguments[i].SemanticType
                ?? throw new SemanticException(
                    $"Argument {i + 1} of '{function.Name}' has no semantic type.");

            if (!parameter.Accepts(argumentType.GetType()))
                throw new SemanticException(
                    $"Argument {i + 1} of '{function.Name}' expects {parameter.Type.Name}, got {argumentType.Name}.");
        }
    }

    private static void ValidateTarget(CallExpression expression, FunctionDefinition function)
    {
        if (expression.Target is null)
        {
            if (function.RequiresTarget)
                throw new SemanticException(
                    $"Function '{function.Name}' must be called on a target.");
            return;
        }

        var targetType = expression.Target.SemanticType
            ?? throw new SemanticException(
                $"Target of '{function.Name}' has no semantic type.");

        if (!function.AcceptsTarget(targetType))
            throw new SemanticException(
                $"Function '{function.Name}' cannot be called on {targetType.Name}.");
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

            _ => throw new SemanticException(
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
            if (decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out _))
                return SemanticTypeOptions.Decimal;

            throw new SemanticException($"Number '{text}' is out of range.");
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return SemanticTypeOptions.Int;

        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            return SemanticTypeOptions.Long;

        // Bilangan bulat di atas long masih muat sebagai decimal
        if (decimal.TryParse(text, NumberStyles.None,
                CultureInfo.InvariantCulture, out _))
            return SemanticTypeOptions.Decimal;

        throw new SemanticException($"Number '{text}' is out of range.");
    }
}