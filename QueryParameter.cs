using FunQuery.SemanticTypes;

namespace FunQuery;

/// <summary>
/// Satu parameter yang diberikan host ke sebuah query: tipe semantiknya (dipakai analyzer) dan
/// nilai runtime-nya (dipakai interpreter). Nama parameter ditulis tanpa '@' dan dirujuk di query
/// sebagai <c>@nama</c>.
/// </summary>
public readonly record struct QueryParameter(SemanticType Type, object? Value);
