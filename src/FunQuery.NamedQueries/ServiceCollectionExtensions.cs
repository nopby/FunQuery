using FunQuery.AspNetCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FunQuery.NamedQueries;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Mendaftarkan catalog named query, lookup cache, listener, dan admin service.
    /// Endpoint admin tetap opt-in melalui <see cref="AddFunQueryNamedQueryAdmin"/>.
    /// </summary>
    public static IServiceCollection AddFunQueryNamedQueries(
        this IServiceCollection services,
        Action<NamedQueryStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new NamedQueryStoreOptions();
        configure(options);
        options.Validate();

        services.Replace(ServiceDescriptor.Singleton(options));
        services.AddMemoryCache();
        services.AddSingleton<PostgreSqlNamedQueryStore>();
        services.AddSingleton<INamedQueryStore>(provider =>
            new NamedQueryLookupCache(
                provider.GetRequiredService<PostgreSqlNamedQueryStore>(),
                provider.GetRequiredService<IMemoryCache>(),
                provider.GetRequiredService<NamedQueryStoreOptions>()));
        services.AddSingleton<INamedQueryCacheInvalidator>(provider =>
            (INamedQueryCacheInvalidator)provider.GetRequiredService<INamedQueryStore>());
        services.TryAddSingleton<INamedQueryAdminService, NamedQueryAdminService>();
        services.TryAddSingleton<INamedQueryResolver, NamedQueryResolver>();

        if (options.EnableChangeListener)
            services.AddHostedService<NamedQueryChangeListener>();

        return services;
    }

    /// <summary>Mendaftarkan opsi admin. Mapping endpoint tetap dipanggil eksplisit oleh host.</summary>
    public static IServiceCollection AddFunQueryNamedQueryAdmin(
        this IServiceCollection services,
        Action<NamedQueryAdminOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new NamedQueryAdminOptions();
        configure?.Invoke(options);
        options.Validate();

        services.Replace(ServiceDescriptor.Singleton(options));
        services.TryAddSingleton<
            INamedQueryAdminAuthorizer,
            DenyAllNamedQueryAdminAuthorizer>();

        return services;
    }
}
