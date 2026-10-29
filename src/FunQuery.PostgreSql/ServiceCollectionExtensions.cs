using FunQuery.Sql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FunQuery.PostgreSql;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Mendaftarkan opsi PostgreSQL, dialect, dan satu <see cref="NpgsqlDataSource"/> yang dipakai
    /// bersama. Source sendiri dibuat dengan <see cref="PostgreSqlSource"/> dan didaftarkan ke
    /// <c>AddFunQuerySources</c>.
    /// </summary>
    public static IServiceCollection AddFunQueryPostgreSql(
        this IServiceCollection services,
        Action<PostgreSqlOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PostgreSqlOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<ISqlDialect>(PostgreSqlDialect.Instance);
        services.AddSingleton(_ => NpgsqlDataSource.Create(options.ConnectionString));

        return services;
    }
}
