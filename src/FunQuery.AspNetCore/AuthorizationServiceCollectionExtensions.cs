using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.AspNetCore;

public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>Mendaftarkan authorizer yang dipanggil sebelum cache dan eksekusi query.</summary>
    public static IServiceCollection AddFunQueryAuthorization<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAuthorizer>(
        this IServiceCollection services)
        where TAuthorizer : class, IQueryAuthorizer
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IQueryAuthorizer, TAuthorizer>();
        return services;
    }

    /// <summary>Mendaftarkan penyedia identitas (tenant, app, permission) dari request.</summary>
    public static IServiceCollection AddFunQueryIdentityProvider<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
        this IServiceCollection services)
        where TProvider : class, IQueryIdentityProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IQueryIdentityProvider, TProvider>();
        return services;
    }

    /// <summary>
    /// Mengaktifkan authorizer berbasis permission per source. Source yang tidak punya policy
    /// ditolak, sehingga source baru tidak otomatis terbuka.
    /// </summary>
    public static IServiceCollection AddFunQueryPermissionAuthorizer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<QuerySourcePermissionPolicy>();
        services.AddScoped<IQueryAuthorizer, PermissionQueryAuthorizer>();
        return services;
    }

    /// <summary>Menetapkan permission yang dibutuhkan oleh sebuah source.</summary>
    public static IServiceCollection AddSourcePermission(
        this IServiceCollection services,
        string sourceName,
        string permission)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        services.AddSingleton(new QuerySourcePermission(sourceName, permission));
        return services;
    }
}

/// <summary>Permission yang dibutuhkan oleh satu source.</summary>
public sealed record QuerySourcePermission(string SourceName, string Permission);

/// <summary>Kumpulan policy source; dibangun dari semua <see cref="QuerySourcePermission"/> terdaftar.</summary>
public sealed class QuerySourcePermissionPolicy
{
    private readonly Dictionary<string, string> _permissions = new(StringComparer.Ordinal);

    public QuerySourcePermissionPolicy(IEnumerable<QuerySourcePermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        foreach (var permission in permissions)
        {
            _permissions[permission.SourceName] = permission.Permission;
        }
    }

    public bool TryGet(string sourceName, out string permission) =>
        _permissions.TryGetValue(sourceName, out permission!);
}

/// <summary>Authorizer default: menolak source tanpa policy dan source yang permission-nya tidak dimiliki.</summary>
internal sealed class PermissionQueryAuthorizer : IQueryAuthorizer
{
    private readonly QuerySourcePermissionPolicy _policy;

    public PermissionQueryAuthorizer(QuerySourcePermissionPolicy policy)
    {
        _policy = policy;
    }

    public ValueTask<QueryAuthorizationDecision> AuthorizeAsync(
        QueryAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.SourceName))
        {
            // Inline $source([...]) tidak membaca data service; tetap boleh setelah identitas ada.
            return ValueTask.FromResult(QueryAuthorizationDecision.Allow());
        }

        if (!_policy.TryGet(request.SourceName, out var permission))
        {
            return ValueTask.FromResult(QueryAuthorizationDecision.Forbidden(
                $"Source '{request.SourceName}' is not exposed for querying."));
        }

        return ValueTask.FromResult(request.Identity.HasPermission(permission)
            ? QueryAuthorizationDecision.Allow()
            : QueryAuthorizationDecision.Forbidden(
                $"Permission '{permission}' is required to query '{request.SourceName}'."));
    }
}
