using FunQuery.Enums;
using FunQuery.SemanticTypes;

namespace FunQuery;

public sealed class FunctionDefinition
{
    public required string Name { get; init; }

    public required IReadOnlyList<ParameterDefinition> Parameters { get; init; }

    // Default: dihitung dari Parameters, bisa di-override
    public int RequiredArgumentCount { get; init; } = -1;
    public int MinArguments =>
        RequiredArgumentCount >= 0
            ? RequiredArgumentCount
            : Parameters.Count(p => p.Required);

    public bool IsVariadic { get; init; }

    // Baru: argumen dianalisis di dalam scope field elemen target
    public bool UsesElementScope { get; init; }

    // Aturan target: wajib, boleh, atau dilarang dipanggil pada target (x.$fn(...))
    public TargetRule TargetRule { get; init; } = TargetRule.Optional;

    public Func<SemanticType?, SemanticType> ReturnType { get; init; } =
        _ => SemanticTypeOptions.Unknown;

    // Baru: dipakai jika return type bergantung pada argumen (mis. $source)
    public Func<SemanticType?, IReadOnlyList<SemanticType>, SemanticType>?
    ReturnTypeFromArguments
    { get; init; }

    public Func<SemanticType?, bool> AcceptsTarget { get; init; } =
        _ => true;

    public SemanticType GetReturnType(SemanticType? type) => ReturnType(type);

    public SemanticType GetReturnType(
    SemanticType? target,
    IReadOnlyList<SemanticType> arguments) =>
    ReturnTypeFromArguments?.Invoke(target, arguments) ?? ReturnType(target);
}