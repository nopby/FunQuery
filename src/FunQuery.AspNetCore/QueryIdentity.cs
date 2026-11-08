namespace FunQuery.AspNetCore;

/// <summary>
/// Identitas pemanggil yang dipakai authorizer. Diisi oleh host dari token/claim; FunQuery tidak
/// membaca token sendiri. Nilai null berarti host tidak menyediakannya.
/// </summary>
public sealed record QueryIdentity
{
    public string? TenantId { get; init; }

    public string? AppId { get; init; }

    public string? SubjectId { get; init; }

    /// <summary>Permission yang dimiliki pemanggil, mis. <c>sales.orders.read</c>.</summary>
    public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.Ordinal);
}
