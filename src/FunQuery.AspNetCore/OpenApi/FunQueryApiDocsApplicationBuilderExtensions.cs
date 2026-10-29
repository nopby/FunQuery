using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Scalar.AspNetCore;

namespace FunQuery.AspNetCore;

/// <summary>
/// Header keamanan untuk UI dokumentasi. UI ini hanya untuk developer dan tidak memuat
/// resource eksternal; CSP membatasi script/style ke origin yang sama.
/// </summary>
public static class FunQueryApiDocsApplicationBuilderExtensions
{
    /// <summary>
    /// Menambahkan header keamanan pada request yang menuju <paramref name="path"/>.
    /// Panggil sebelum <c>MapFunQueryEndpoints</c>.
    /// </summary>
    public static IApplicationBuilder UseFunQueryApiDocsSecurityHeaders(
        this IApplicationBuilder app,
        string path = "/docs")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(path))
            {
                await next(context);
                return;
            }

            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";

            // Scalar membuat nonce per request dan menyimpannya di HttpContext.Items.
            // OnStarting dipakai karena endpoint Scalar menulis respons setelah middleware ini.
            context.Response.OnStarting(() =>
            {
                var nonce = context.Items.TryGetValue(
                    ScalarOptions.NonceHttpContextItemKey,
                    out var value)
                    ? value as string
                    : null;

                var scriptSource = string.IsNullOrEmpty(nonce)
                    ? "script-src 'self' 'unsafe-inline'"
                    : $"script-src 'self' 'nonce-{nonce}'";

                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    $"{scriptSource}; " +
                    "style-src 'self' 'unsafe-inline'; " +
                    "img-src 'self' data:; " +
                    "font-src 'self' data:; " +
                    "connect-src 'self'; " +
                    "base-uri 'none'; " +
                    "form-action 'none'; " +
                    "frame-ancestors 'none'";

                return Task.CompletedTask;
            });

            await next(context);
        });
    }
}
