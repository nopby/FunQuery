using Microsoft.AspNetCore.Http;

namespace FunQuery.AspNetCore;

/// <summary>Data yang tersedia untuk authorizer sebelum query dijalankan.</summary>
public sealed record QueryAuthorizationRequest
{
    /// <summary>Source yang diminta; null bila query memakai <c>$source([...])</c> inline.</summary>
    public required string? SourceName { get; init; }

    /// <summary>Teks query setelah <c>$named</c> di-resolve.</summary>
    public required string Query { get; init; }

    public required QueryIdentity Identity { get; init; }

    public required HttpContext HttpContext { get; init; }
}

/// <summary>Keputusan authorizer. Default-nya menolak; host harus memilih secara eksplisit.</summary>
public sealed record QueryAuthorizationDecision
{
    private QueryAuthorizationDecision(bool isAllowed, bool isUnauthenticated, string? message)
    {
        IsAllowed = isAllowed;
        IsUnauthenticated = isUnauthenticated;
        Message = message;
    }

    public bool IsAllowed { get; }

    public bool IsUnauthenticated { get; }

    public string? Message { get; }

    public static QueryAuthorizationDecision Allow() => new(true, false, null);

    public static QueryAuthorizationDecision Unauthenticated(string message = "Authentication is required.")
        => new(false, true, message);

    public static QueryAuthorizationDecision Forbidden(string message = "Access denied.")
        => new(false, false, message);
}

/// <summary>
/// Otorisasi query. Bila didaftarkan, adapter memanggilnya untuk setiap request **sebelum** cache
/// dan eksekusi; penolakan menjadi <c>401</c> (unauthenticated) atau <c>403</c> (forbidden).
/// </summary>
public interface IQueryAuthorizer
{
    ValueTask<QueryAuthorizationDecision> AuthorizeAsync(
        QueryAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}
