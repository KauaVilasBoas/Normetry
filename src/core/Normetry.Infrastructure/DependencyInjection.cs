using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Establishments;
using Normetry.Application.Identity;
using Normetry.Application.Inspections;
using Normetry.Application.Tenancy;
using Normetry.Application.Usage;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure.Messaging;
using Normetry.Infrastructure.Persistence;
using Normetry.Infrastructure.Storage;

namespace Normetry.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The core database as <c>app_core</c> (<c>ConnectionStrings:core</c>, required), scoped to
    /// the request's <see cref="ITenantContext"/>, plus the clock, the repositories, the transaction
    /// behavior, a startup check that the role cannot get around RLS, the outbox and its
    /// dispatcher to RabbitMQ (<c>ConnectionStrings:rabbitmq</c>, required) and the document blob
    /// store (<c>ConnectionStrings:blobs</c>, required). Call it after
    /// <c>AddDispatcher</c>: the behavior then runs inside logging and validation, so an invalid
    /// request opens no transaction.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // The one clock of the handlers, the outbox and its dispatcher, and the inbox (daily
        // counts, due dates, timeouts). Tests replace it.
        services.AddSingleton(TimeProvider.System);
        AddCoreDatabase(services, configuration);
        AddRepositories(services);
        AddDocumentBlobStore(services, configuration);
        AddBackgroundScopes(services);
        AddMessaging(services);
        return services;
    }

    /// <summary>
    /// Development only: creates the demo tenants of the local Keycloak realm and their demo
    /// establishments.
    /// </summary>
    public static IServiceCollection AddDemoTenantSeed(this IServiceCollection services) =>
        services.AddHostedService<DemoTenantSeed>();

    /// <summary>
    /// Development only: once the API has started, uploads the demo establishments' documents from
    /// <c>evals/establishments</c> through the upload commands.
    /// </summary>
    public static IServiceCollection AddDemoDocumentSeed(this IServiceCollection services) =>
        services.AddHostedService<DemoDocumentSeed>();

    /// <summary>
    /// The tenant-scoped context, the transaction every request runs in, and the role check, which
    /// is registered before the demo seed and the messaging services, so none of them runs on a
    /// privileged role.
    /// </summary>
    private static void AddCoreDatabase(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = CoreDatabase.GetConnectionString(configuration);
        services.AddDbContext<CoreDbContext>(
            (provider, options) =>
                options.UseTenantScopedCoreDatabase(
                    connectionString,
                    provider.GetRequiredService<ITenantContext>()
                )
        );
        services.AddHostedService<RuntimeRoleCheck>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddScoped<AfterCommit>();
    }

    private static void AddRepositories(IServiceCollection services)
    {
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IEstablishmentRepository, EstablishmentRepository>();
        services.AddScoped<IChatMessageCounter, ChatMessageCounter>();
        services.AddScoped<IInspectionStartCounter, InspectionStartCounter>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IInspectionRepository, InspectionRepository>();
    }

    private static void AddDocumentBlobStore(
        IServiceCollection services,
        IConfiguration configuration
    )
    {
        var connectionString = configuration.GetConnectionString(
            BlobDocumentStore.ConnectionStringName
        )
            ?? throw new InvalidOperationException(
                $"Set ConnectionStrings:{BlobDocumentStore.ConnectionStringName} (the blob storage account)."
            );
        var container = configuration[BlobDocumentStore.ContainerSetting]
            ?? BlobDocumentStore.DefaultContainer;
        services.AddSingleton<IDocumentBlobStore>(_ => new BlobDocumentStore(
            new BlobContainerClient(connectionString, container)
        ));
    }

    /// <summary>
    /// Background services open one scope per tenant and set its tenant here first; the
    /// development document seed also sets the user it uploads as.
    /// </summary>
    private static void AddBackgroundScopes(IServiceCollection services)
    {
        services.AddScoped<BackgroundTenant>();
        services.AddScoped<BackgroundUser>();
    }

    /// <summary>
    /// The outbox and its dispatcher, the inbox, both status consumers and the timeout sweeper.
    /// </summary>
    private static void AddMessaging(IServiceCollection services)
    {
        services.AddScoped<IOutbox, Outbox>();
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqPublisher>();
        // The tenants the dispatcher and the sweeper visit, as core_dispatcher: its connection
        // string is read once, when the first of them starts.
        services.AddSingleton(provider => new OutboxTenantList(
            CoreDatabase.GetDispatcherConnectionString(
                provider.GetRequiredService<IConfiguration>()
            )
        ));
        services.AddHostedService<OutboxDispatcher>();
        services.AddScoped<IInbox, Inbox>();
        services.AddSingleton<DocumentStatusMessages>();
        services.AddHostedService<DocumentStatusConsumer>();
        services.AddSingleton<InspectionStatusMessages>();
        services.AddHostedService<InspectionStatusConsumer>();
        services.AddHostedService<TimeoutSweeper>();
    }
}
