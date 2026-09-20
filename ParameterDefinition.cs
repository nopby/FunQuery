using FunQuery.SemanticTypes;

namespace FunQuery;

public sealed class ParameterDefinition
{
    public required string Name { get; init; }

    /// <summary>Nama tipe yang diharapkan, untuk pesan error (mis. "array", "bool").</summary>
    public required string Expected { get; init; }

    /// <summary>Aturan tipe: true bila tipe argumen dapat diterima parameter ini.</summary>
    public required Func<SemanticType, bool> Accepts { get; init; }

    public bool Required { get; init; } = true;

    /// <summary>Parameter yang menerima tepat satu jenis tipe semantik.</summary>
    public static ParameterDefinition Of<TType>(string name, string expected, bool required = true)
        where TType : SemanticType =>
        new()
        {
            Name = name,
            Expected = expected,
            Accepts = static type => type is TType,
            Required = required,
        };
}
