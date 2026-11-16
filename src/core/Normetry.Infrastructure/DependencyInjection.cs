using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Establishments;
using Normetry.Application.Identity;
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
    /// the request's <see cref="ITenantContext"/>, plus the repositories, the transaction
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
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IEstablishmentRepository, EstablishmentRepository>();
        services.AddScoped<IChatMessageCounter, ChatMessageCounter>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        var blobConnectionString = configuration.GetConnectionString(
            BlobDocumentStore.ConnectionStringName
        )
            ?? throw new InvalidOperationException(
                $"Set ConnectionStrings:{BlobDocumentStore.ConnectionStringName} (the blob storage account)."
            );
        var blobContainer = configuration["Documents:BlobContainer"] ?? "documents";
        services.AddSingleton<IDocumentBlobStore>(_ => new BlobDocumentStore(
            new BlobContainerClient(blobConnectionString, blobContainer)
        ));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // Background services open one scope per tenant and set its tenant here first.
        services.AddScoped<BackgroundTenant>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqPublisher>();
        services.AddHostedService<OutboxDispatcher>();
        services.AddScoped<IInbox, Inbox>();
        services.AddSingleton<DocumentStatusMessages>();
        services.AddHostedService<DocumentStatusConsumer>();
        services.AddHostedService<ProcessingTimeoutSweeper>();
        return services;
    }

    /// <summary>
    /// Development only: creates the demo tenants of the local Keycloak realm and their demo
    /// establishments.
    /// </summary>
    public static IServiceCollection AddDemoTenantSeed(this IServiceCollection services) =>
        services.AddHostedService<DemoTenantSeed>();
}
