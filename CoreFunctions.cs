using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery;

/// <summary>
/// Function inti bahasa. Didaftarkan lewat mekanisme ekstensi yang sama dengan ekstensi pihak lain.
/// </summary>
public sealed class CoreFunctions : IQueryExtension
{
    public string Name => "core";

    public void Register(FunctionRegistry registry)
    {
        registry.Add(new FunctionDefinition
        {
            Name = "$source",
            Parameters = [ParameterDefinition.Of<ArrayType>("source", "array")],
            TargetRule = TargetRule.Forbidden,
            ReturnTypeFromArguments = (_, args) => args[0],
        });

        registry.Add(new FunctionDefinition
        {
            Name = "$filter",
            Parameters = [ParameterDefinition.Of<BooleanType>("predicate", "bool")],
            TargetRule = TargetRule.Required,
            UsesElementScope = true,
            AcceptsTarget = t => t is ArrayType,
            ReturnType = target => target ?? SemanticTypeOptions.Unknown,
        });
    }
}
