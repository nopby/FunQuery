using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FunQuery.Server.Playground;

public static class PlaygroundApplicationBuilderExtensions
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>
    /// Header keamanan untuk halaman playground. Hanya berlaku pada path <c>/playground</c>,
    /// sehingga respons API tidak terpengaruh.
    /// </summary>
    public static IApplicationBuilder UsePlaygroundSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/playground"))
            {
                context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
            }

            await next(context);
        });
    }

    /// <summary>Mengarahkan <c>/playground</c> ke <c>/playground/</c> agar default file bekerja.</summary>
    public static IApplicationBuilder UsePlaygroundRedirect(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals(new PathString("/playground")))
            {
                context.Response.Redirect("/playground/");
                return;
            }

            await next(context);
        });
    }
}
