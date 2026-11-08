using Microsoft.AspNetCore.Http;

namespace FunQuery.NamedQueries;

public sealed class NamedQueryAdminOptions
{
    /// <summary>Prefix for the opt-in admin endpoints.</summary>
    public string Route { get; set; } = "/funquery/admin/named-queries";

    /// <summary>Maximum page size for list requests.</summary>
    public int MaxPageSize { get; set; } = 200;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Route) || !Route.StartsWith('/'))
            throw new ArgumentException(
                $"{nameof(Route)} must be a non-empty path starting with '/'.",
                nameof(Route));

        if (MaxPageSize < 1)
            throw new ArgumentOutOfRangeException(
                nameof(MaxPageSize),
                MaxPageSize,
                $"{nameof(MaxPageSize)} must be at least 1.");
    }
}

public interface INamedQueryAdminAuthorizer
{
    ValueTask<bool> CanManageAsync(
        HttpContext context,
        CancellationToken cancellationToken);
}

/// <summary>Default deny; the host must replace this with its own policy/Keycloak check.</summary>
public sealed class DenyAllNamedQueryAdminAuthorizer : INamedQueryAdminAuthorizer
{
    public ValueTask<bool> CanManageAsync(
        HttpContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
