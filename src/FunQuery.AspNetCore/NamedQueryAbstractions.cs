using Microsoft.AspNetCore.Http;

namespace FunQuery.AspNetCore;

/// <summary>Identitas catalog yang dipakai untuk me-resolve named query.</summary>
public readonly record struct NamedQueryScope(string AppId, string TenantId);

/// <summary>Host menentukan app/tenant dari claim atau konteks server-side.</summary>
public interface INamedQueryScopeProvider
{
    NamedQueryScope GetScope(HttpContext context);
}

/// <summary>Default untuk host single-app/single-tenant.</summary>
public sealed class GlobalNamedQueryScopeProvider : INamedQueryScopeProvider
{
    public NamedQueryScope GetScope(HttpContext context) => new("global", "global");
}

/// <summary>Hasil resolusi catalog; provider tidak perlu tahu asal key.</summary>
public sealed record NamedQueryResolution(
    string Key,
    string Query,
    string? SourceName,
    string AppId,
    string TenantId,
    int Version);

/// <summary>
/// Disediakan oleh modul named query. Adapter hanya bergantung pada abstraksi ini agar
/// FunQuery.AspNetCore tidak perlu mereferensikan project store PostgreSQL.
/// </summary>
public interface INamedQueryResolver
{
    ValueTask<NamedQueryResolution> ResolveAsync(
        string appId,
        string tenantId,
        string key,
        CancellationToken cancellationToken);
}
