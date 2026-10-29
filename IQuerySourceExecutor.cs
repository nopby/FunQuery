namespace FunQuery;

/// <summary>
/// Eksekutor untuk source yang tidak dimuat ke memori, mis. PostgreSQL. Adapter HTTP memanggil
/// ini setelah query dianalisis terhadap schema source.
/// </summary>
public interface IQuerySourceExecutor
{
    ValueTask<object?> ExecuteAsync(
        QueryPlan plan,
        QueryEnvironment environment,
        CancellationToken cancellationToken = default);
}
