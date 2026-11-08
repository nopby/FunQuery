using Microsoft.AspNetCore.Http;

namespace FunQuery.AspNetCore;

/// <summary>
/// Menyediakan <see cref="QueryIdentity"/> untuk request saat ini. Host multi-tenant harus
/// mendaftarkan implementasi ini; pakai klaim token, jangan parameter query.
/// </summary>
public interface IQueryIdentityProvider
{
    ValueTask<QueryIdentity?> ResolveAsync(HttpContext context, CancellationToken cancellationToken = default);
}
