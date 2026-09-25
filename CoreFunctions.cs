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
            // $let(@nama, nilai): dianalisis dan dieksekusi secara khusus (lihat
            // SemanticAnalyzer.AnalyzeLet dan CoreInMemoryFunctions.Let). Didaftarkan di sini
            // hanya supaya "$let" dikenal sebagai nama function (whitelist, pesan
            // UNKNOWN_FUNCTION yang konsisten), bukan supaya validasi generik berlaku padanya.
            Name = "$let",
            Parameters = [],
            TargetRule = TargetRule.Optional,
        });

        registry.Add(new FunctionDefinition
        {
            // $field(name) atau $field(@variable): dianalisis dan dieksekusi secara khusus
            // (lihat SemanticAnalyzer.AnalyzeField dan CoreInMemoryFunctions.Field).
            // TargetRule.Forbidden supaya tidak bisa dirantai pada data (x.$field(...)),
            // sama seperti $source, tapi tetap transparan terhadap $let di depannya.
            Name = "$field",
            Parameters = [],
            TargetRule = TargetRule.Forbidden,
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

        registry.Add(new FunctionDefinition
        {
            // $select(id, name) atau $select({...}): dianalisis secara khusus (lihat
            // SemanticAnalyzer.AnalyzeSelect), karena nama key hasilnya harus diambil dari
            // AST argumen (nama field), bukan hanya dari tipenya. Aturan target (wajib array)
            // tetap divalidasi generik lewat TargetRule di sini.
            Name = "$select",
            Parameters = [],
            TargetRule = TargetRule.Required,
            AcceptsTarget = t => t is ArrayType,
        });

        registry.Add(new FunctionDefinition
        {
            Name = "$map",
            Parameters =
            [
                new ParameterDefinition
                {
                    Name = "expression",
                    Expected = "any",
                    Accepts = static _ => true,
                },
            ],
            TargetRule = TargetRule.Required,
            UsesElementScope = true,
            AcceptsTarget = t => t is ArrayType,
            ReturnTypeFromArguments = (_, args) => new ArrayType(args[0]),
        });
    }
}
