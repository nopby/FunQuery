using Console.Expressions;
using Console.SemanticTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Console;

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
            GetValueType(expression);

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
        // Analyze target terlebih dahulu.
        if (expression.Target is not null)
        {
            Analyze(
                expression.Target,
                context);
        }

        // Analyze arguments.
        foreach (var argument in expression.Arguments)
        {
            Analyze(
                argument,
                context);
        }

        var functionName =
            context.GetFunctionName(
                expression.Function);

        var function =
            context.ResolveFunction(
                functionName);

        if (function is null)
        {
            throw new SemanticException(
                $"Unknown function '{functionName}'.");
        }

        ValidateArguments(
            expression,
            function);

        ValidateTarget(
            expression,
            function);

        var targetType =
            expression.Target?.SemanticType;

        expression.SemanticType =
            function.GetReturnType(
                targetType);

        return expression;
    }

    private static void ValidateArguments(
        CallExpression expression,
        FunctionDefinition function)
    {
        if (expression.Arguments.Count <
            function.RequiredArgumentCount)
        {
            throw new SemanticException(
                $"Function '{function.Name}' " +
                $"requires at least " +
                $"{function.RequiredArgumentCount} argument(s).");
        }

        if (!function.IsVariadic &&
            expression.Arguments.Count >
            function.Parameters.Count)
        {
            throw new SemanticException(
                $"Function '{function.Name}' " +
                $"accepts at most " +
                $"{function.Parameters.Count} argument(s).");
        }

        for (int i = 0;
             i < expression.Arguments.Count;
             i++)
        {
            var argument =
                expression.Arguments[i];

            var parameter =
                function.Parameters[i];

            var argumentType =
                argument.SemanticType
                ?? throw new SemanticException(
                    $"Argument {i + 1} of " +
                    $"'{function.Name}' has no semantic type.");

            if (!parameter.Accepts(argumentType))
            {
                throw new SemanticException(
                    $"Argument {i + 1} of " +
                    $"'{function.Name}' expects " +
                    $"{parameter.Type.Name}, " +
                    $"got {argumentType.Name}.");
            }
        }
    }

    private static void ValidateTarget(
        CallExpression expression,
        FunctionDefinition function)
    {
        if (expression.Target is null)
            return;

        var targetType =
            expression.Target.SemanticType
            ?? throw new SemanticException(
                $"Target of '{function.Name}' " +
                "has no semantic type.");

        if (!function.AcceptsTarget(targetType))
        {
            throw new SemanticException(
                $"Function '{function.Name}' " +
                $"cannot be called on " +
                $"{targetType.Name}.");
        }
    }

    private static SemanticType GetValueType(
        ValueExpression expression)
    {
        return expression.Token.Type switch
        {
            TokenType.StringLiteral =>
                SemanticTypeOptions.String,

            TokenType.IntegerLiteral =>
                SemanticTypeOptions.Int,

            TokenType.LongLiteral =>
                SemanticTypeOptions.Long,

            TokenType.FloatLiteral =>
                SemanticTypeOptions.Float,

            TokenType.DoubleLiteral =>
                SemanticTypeOptions.Double,

            TokenType.DecimalLiteral =>
                SemanticTypeOptions.Decimal,

            _ => throw new SemanticException(
                $"Unsupported literal type: " +
                $"{expression.Token.Type}.")
        };
    }
}