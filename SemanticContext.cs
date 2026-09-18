using Console.Expressions;
using Console.SemanticTypes;
using System.Security.AccessControl;

namespace Console;

public sealed class SemanticContext
{
    private readonly ReadOnlyMemory<char> _source;

    private readonly IReadOnlyDictionary<string, SemanticType>
        _identifiers;

    private readonly IReadOnlyDictionary<string, FunctionDefinition>
        _functions;

    public SemanticContext(
        ReadOnlyMemory<char> source,
        IReadOnlyDictionary<string, SemanticType> identifiers,
        IReadOnlyDictionary<string, FunctionDefinition> functions)
    {
        _source = source;
        _identifiers = identifiers;
        _functions = functions;
    }

    public string GetFunctionName(Token token)
    {
        return _source.Span[
            token.StartPosition..token.EndPosition]
            .ToString();
    }

    public string GetIdentifierName(
        IdentifierExpression expression)
    {
        var token = expression.Token;

        return _source.Span[
            token.StartPosition..token.EndPosition]
            .ToString();
    }

    public SemanticType? ResolveIdentifier(
        string name)
    {
        return _identifiers.TryGetValue(
            name,
            out var type)
            ? type
            : null;
    }

    public FunctionDefinition? ResolveFunction(
        string name)
    {
        return _functions.TryGetValue(
            name,
            out var function)
            ? function
            : null;
    }

    public SemanticType ResolveArrayType(
        ArrayExpression expression)
    {
        if (expression.Elements.Count == 0)
        {
            return new ArrayType(
                SemanticTypeOptions.Unknown);
        }

        var firstType =
            expression.Elements[0].SemanticType
            ?? throw new SemanticException(
                "Array element has no semantic type.");

        for (int i = 1;
             i < expression.Elements.Count;
             i++)
        {
            var elementType =
                expression.Elements[i].SemanticType
                ?? throw new SemanticException(
                    $"Array element at index {i} " +
                    "has no semantic type.");

            if (!AreCompatible(
                    firstType,
                    elementType))
            {
                throw new SemanticException(
                    $"Array elements must have compatible types. " +
                    $"Expected {firstType.Name}, " +
                    $"got {elementType.Name} at index {i}.");
            }
        }

        return new ArrayType(firstType);
    }

    public SemanticType ResolveBlockType(
        BlockExpression expression)
    {
        if (expression.Expressions.Count == 0)
        {
            return SemanticTypeOptions.Void;
        }

        return expression.Expressions[^1].SemanticType
            ?? throw new SemanticException(
                "Last expression in block " +
                "has no semantic type.");
    }

    public bool CanCompare(
        SemanticType left,
        SemanticType right)
    {
        if (left == SemanticTypeOptions.Unknown ||
            right == SemanticTypeOptions.Unknown)
        {
            return false;
        }

        if (AreCompatible(left, right))
        {
            return true;
        }

        return IsNumeric(left) &&
               IsNumeric(right);
    }

    private static bool AreCompatible(
        SemanticType left,
        SemanticType right)
    {
        if (left == right)
            return true;

        if (left is AnyType ||
            right is AnyType)
        {
            return true;
        }

        return false;
    }

    private static bool IsNumeric(
        SemanticType type)
    {
        return type switch
        {
            IntType => true,
            LongType => true,
            FloatType => true,
            DoubleType => true,
            DecimalType => true,
            _ => false
        };
    }
}