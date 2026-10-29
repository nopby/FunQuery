using Microsoft.Extensions.DependencyInjection;

namespace FunQuery.AspNetCore;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Mendaftarkan <see cref="QueryEngine"/> dan <see cref="FunQueryOptions"/> yang dipakai
    /// endpoint FunQuery. Panggil sekali saat startup; engine aman dipakai bersama antar-request.
    /// </summary>
    public static IServiceCollection AddFunQuery(
        this IServiceCollection services,
        Action<FunQueryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new FunQueryOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(new QueryEngine(options.Limits));

        return services;
    }
}
