using FunQuery.Sql.Plan;

namespace FunQuery.Sql;

/// <summary>
/// Kontrak executor provider. Implementasi pertama adalah SQL/PostgreSQL; interpreter in-memory
/// tetap menjadi referensi semantik dan bisa diadaptasi ke kontrak ini nanti.
/// </summary>
public interface IQueryExecutor
{
    ProviderCapabilities Capabilities { get; }

    ValueTask<object?> ExecuteAsync(
        RelationalPlan plan,
        QueryEnvironment environment,
        CancellationToken cancellationToken = default);
}
