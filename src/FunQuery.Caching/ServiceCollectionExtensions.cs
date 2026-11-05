using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FunQuery.Caching;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Mendaftarkan L1 memory cache, key builder, metrics, dan single-flight cache.
    /// L2 hanya aktif bila host juga mendaftarkan <c>IDistributedCache</c>.
    /// </summary>
    public static IServiceCollection AddFunQueryQueryCache(
        this IServiceCollection services,
        Action<QueryCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new QueryCacheOptions();
        configure?.Invoke(options);
        options.Validate();

        services.Replace(ServiceDescriptor.Singleton(options));
        services.AddMemoryCache(cache => cache.SizeLimit = options.MaxEntries);
        services.TryAddSingleton<QueryCacheMetrics>();
        services.TryAddSingleton<IQueryCacheKeyBuilder, QueryCacheKeyBuilder>();
        services.TryAddSingleton<IQueryResultCache, QueryResultCache>();

        return services;
    }
}
