using Microsoft.AspNetCore.Http;

namespace FunQuery.AspNetCore;

/// <summary>
/// Menyediakan scope cache per request (misalnya tenant atau user). Host multi-tenant wajib
/// mendaftarkan implementasi ini agar hasil query tidak tercampur antar-tenant.
/// </summary>
public interface IQueryCacheScopeProvider
{
    /// <summary>
    /// Mengembalikan scope stabil untuk request ini, atau null bila host tidak memakai scope.
    /// Jangan mengembalikan token/rahasia mentah; gunakan identifier yang stabil.
    /// </summary>
    string? GetScope(HttpContext context);
}
