using FunQuery.SemanticTypes;

namespace FunQuery.Sql.Sql;

/// <summary>Parameter bertipe yang dikirim bersama SQL. Nilai tidak pernah dirangkai ke teks SQL.</summary>
public sealed record SqlParameter(
    string Name,
    object? Value,
    SemanticType Type);

public sealed record SqlStatement(
    string Sql,
    IReadOnlyList<SqlParameter> Parameters);
