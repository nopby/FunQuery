using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FunQuery.AspNetCore;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Mendaftarkan <see cref="QueryEngine"/>, <see cref="FunQueryOptions"/>, dan registry source
    /// kosong. Panggil sekali saat startup; engine aman dipakai bersama antar-request.
    /// </summary>
    public static IServiceCollection AddFunQuery(
        this IServiceCollection services,
        Action<FunQueryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new FunQueryOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(SourceRegistry.Empty);
        services.AddSingleton(options);
        services.AddSingleton(new QueryEngine(options.Limits));

        return services;
    }

    /// <summary>
    /// Mendaftarkan source yang boleh diakses query. Registry dibekukan setelah konfigurasi, dan
    /// penggantian registry yang sudah ada dimungkinkan agar test/host bisa memilih source sendiri.
    /// </summary>
    public static IServiceCollection AddFunQuerySources(
        this IServiceCollection services,
        Action<SourceRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var registry = new SourceRegistry();
        configure(registry);
        registry.Freeze();

        services.AddSingleton(registry);

        return services;
    }
}
