namespace FunQuery.SemanticTypes;

public abstract record SemanticType
{
    public abstract string Name { get; }
}


public static class SemanticTypeOptions
{
    public static readonly SemanticType Int =
        new IntType();

    public static readonly SemanticType String =
        new StringType();

    public static readonly SemanticType Boolean =
        new BooleanType();

    public static readonly SemanticType Float =
        new FloatType();

    public static readonly SemanticType Decimal =
        new DecimalType();
    public static readonly SemanticType Long =
        new LongType();
    public static readonly SemanticType Double =
        new DoubleType();
    public static readonly SemanticType Unknown =
        new UnknownType();
    public static readonly SemanticType Void =
        new VoidType();
    public static readonly SemanticType Null =
        new NullType();
    public static readonly SemanticType AnyType =
        new AnyType();
    public static SemanticType Array(SemanticType type) =>
        new ArrayType(type);
}