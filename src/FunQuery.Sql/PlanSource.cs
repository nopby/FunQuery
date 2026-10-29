using FunQuery.SemanticTypes;

namespace FunQuery.Sql;

/// <summary>
/// Source yang bisa dipakai plan builder. Variabel <c>@nama</c> di query dipetakan ke source ini.
/// </summary>
public sealed record PlanSource(string Name, ObjectType Schema);
