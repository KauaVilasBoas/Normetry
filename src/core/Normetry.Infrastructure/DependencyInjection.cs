using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Abstractions;
using Normetry.Application.Establishments;
using Normetry.Application.Usage;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The core database as <c>app_core</c> (<c>ConnectionStrings:core</c>, required), scoped to
    /// the request's <see cref="ITenantContext"/>, plus the repositories, the transaction
    /// behavior and a startup check that the role cannot get around RLS. Call it after <c>AddDispatcher</c>: the behavior then runs inside logging and
    /// validation, so an invalid request opens no transaction.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString = CoreDatabase.GetConnectionString(configuration);
        services.AddDbContext<CoreDbContext>(
            (provider, options) =>
                options.UseTenantScopedCoreDatabase(
                    connectionString,
                    provider.GetRequiredService<ITenantContext>()
                )
        );
        // Registered before the demo seed, so the seed never runs on a privileged role.
        services.AddHostedService<RuntimeRoleCheck>();
        services.AddScoped<IEstablishmentRepository, EstablishmentRepository>();
        services.AddScoped<IChatMessageCounter, ChatMessageCounter>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        return services;
    }

    /// <summary>Development only: creates the demo tenants of the local Keycloak realm.</summary>
    public static IServiceCollection AddDemoTenantSeed(this IServiceCollection services) =>
        services.AddHostedService<DemoTenantSeed>();
}
