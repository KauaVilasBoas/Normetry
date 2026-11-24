using Aspire.Hosting.Azure;
using Aspire.Hosting.JavaScript;
using Aspire.Hosting.Python;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Normetry.Infrastructure;
using static Normetry.AppHost.AppHostConstants;

namespace Normetry.AppHost;

/// <summary>The steps of <c>AppHost.cs</c>: each adds one Normetry resource with what it needs.</summary>
internal static class NormetryResources
{
    private const string AnthropicApiKey = "anthropic-api-key";

    private const string AiHealthCheck = $"{HostingKeys.AiService}-health";

    /// <summary>
    /// Generated on the first run and kept in the AppHost user secrets, so a fresh clone needs no
    /// setup. Letters and digits only: the values go unescaped into connection strings and URLs.
    /// </summary>
    public static IResourceBuilder<ParameterResource> AddGeneratedSecret(
        this IDistributedApplicationBuilder builder,
        string name
    ) =>
        builder.AddParameter(
            name,
            new GenerateParameterDefault { MinLength = 32, Special = false },
            secret: true,
            persist: true
        );

    public static DatabasePasswords AddDatabasePasswords(
        this IDistributedApplicationBuilder builder
    ) =>
        new(
            builder.AddGeneratedSecret("core-owner-password"),
            builder.AddGeneratedSecret("ai-owner-password"),
            builder.AddGeneratedSecret("app-core-password"),
            builder.AddGeneratedSecret("core-dispatcher-password"),
            builder.AddGeneratedSecret("app-ai-password"),
            builder.AddGeneratedSecret("corpus-ingestor-password")
        );

    /// <summary>
    /// The init script runs only on an empty data volume: it creates the roles and both schemas.
    /// </summary>
    public static IResourceBuilder<PostgresServerResource> AddNormetryPostgres(
        this IDistributedApplicationBuilder builder,
        DatabasePasswords passwords
    ) =>
        builder
            .AddPostgres("postgres")
            .WithImage("pgvector/pgvector", "0.8.7-pg17")
            .WithDataVolume()
            .WithInitFiles($"{DeployDirectory}/postgres/init")
            .WithEnvironment("POSTGRES_DB", Database)
            .WithEnvironment("CORE_OWNER_PASSWORD", passwords.CoreOwner)
            .WithEnvironment("AI_OWNER_PASSWORD", passwords.AiOwner)
            .WithEnvironment("APP_CORE_PASSWORD", passwords.AppCore)
            .WithEnvironment("CORE_DISPATCHER_PASSWORD", passwords.CoreDispatcher)
            .WithEnvironment("APP_AI_PASSWORD", passwords.AppAi)
            .WithEnvironment("CORPUS_INGESTOR_PASSWORD", passwords.CorpusIngestor);

    /// <summary>
    /// Short-lived: applies the core migrations as core_owner and exits. Only this step gets the
    /// owner connection string, because the owner can switch RLS off; the API connects as app_core.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddCoreMigrations(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresServerResource> postgres,
        IResourceBuilder<ParameterResource> coreOwnerPassword
    ) =>
        builder
            .AddProject<Projects.Normetry_Migrations>("core-migrate")
            .WithEnvironment(
                HostingKeys.ConnectionStringVariableOf(HostingKeys.MigrationsConnectionString),
                Npgsql(postgres, CoreOwnerRole, coreOwnerPassword)
            )
            .WaitFor(postgres);

    /// <summary>
    /// Short-lived: applies the ai migrations as ai_owner and exits. Only this step gets the owner
    /// URL, because the owner can switch RLS off; the running service connects as app_ai.
    /// </summary>
    public static IResourceBuilder<ExecutableResource> AddAiMigrations(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresServerResource> postgres,
        IResourceBuilder<ParameterResource> aiOwnerPassword
    ) =>
        builder
            .AddExecutable("ai-migrate", "uv", AiDirectory, "run", "alembic", "upgrade", "head")
            .WithEnvironment(
                "NORMETRY_MIGRATIONS_DATABASE_URL",
                Psycopg(postgres, AiOwnerRole, aiOwnerPassword)
            )
            .WaitFor(postgres);

    /// <summary>
    /// On demand (start it from the dashboard): loads corpus/manifest.json into ai.sources and
    /// ai.chunks. corpus_ingestor is the only role that writes the public corpus, so only this step
    /// gets its URL. Fetch the corpus first (corpus/scripts/fetch_corpus.py); the first run also
    /// downloads the embedding model (about 2.3 GB) into the Hugging Face cache.
    /// </summary>
    public static IResourceBuilder<ExecutableResource> AddCorpusIngest(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresServerResource> postgres,
        IResourceBuilder<ParameterResource> corpusIngestorPassword,
        IResourceBuilder<ExecutableResource> aiMigrate
    ) =>
        builder
            .AddExecutable(
                "corpus-ingest",
                "uv",
                AiDirectory,
                "run",
                "python",
                "-m",
                "normetry_ai.ingestion.ingest"
            )
            .WithEnvironment(
                "NORMETRY_CORPUS_DATABASE_URL",
                Psycopg(postgres, CorpusIngestorRole, corpusIngestorPassword)
            )
            .WithExplicitStart()
            .WaitFor(postgres)
            .WaitForCompletion(aiMigrate);

    public static IResourceBuilder<UvicornAppResource> AddAiService(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresServerResource> postgres,
        IResourceBuilder<ParameterResource> appAiPassword,
        IResourceBuilder<ParameterResource> internalToken,
        IResourceBuilder<RabbitMQServerResource> rabbitmq,
        IResourceBuilder<AzureBlobStorageResource> blobs,
        IResourceBuilder<ExecutableResource> aiMigrate
    )
    {
        var ai = builder
            .AddUvicornApp(HostingKeys.AiService, AiDirectory, "normetry_ai.api:create_app")
            .WithUv()
            // Plain http on loopback, so the API gets services__ai__http__0 even with a trusted
            // dev cert.
            .WithoutHttpsCertificate()
            // AddUvicornApp adds --reload; without --reload-dir it would poll every file under
            // .venv too.
            .WithArgs("--factory", "--reload-dir", "normetry_ai")
            .WithEnvironment("NORMETRY_INTERNAL_TOKEN", internalToken)
            .WithEnvironment("NORMETRY_DATABASE_URL", Psycopg(postgres, AppAiRole, appAiPassword))
            // Setting the RabbitMQ URL turns the service's document consumer on.
            .WithEnvironment("NORMETRY_RABBITMQ_URL", rabbitmq)
            // Read only: a SAS for the documents container with read permission and nothing else,
            // made from the account key when the service starts (ReadOnlyBlobAccess). Only the API
            // gets the key.
            .WithEnvironment(async context =>
                context.EnvironmentVariables["NORMETRY_BLOB_CONNECTION_STRING"] =
                    ReadOnlyBlobAccess.ConnectionString(
                        await blobs.Resource.ConnectionStringExpression.GetValueAsync(
                            context.CancellationToken
                        )
                            ?? throw new InvalidOperationException(
                                "The blobs connection string is empty."
                            ),
                        DocumentsContainer,
                        DateTimeOffset.UtcNow.AddYears(1)
                    )
            )
            .WithEnvironment("NORMETRY_BLOB_CONTAINER", DocumentsContainer)
            .WaitFor(postgres)
            .WaitFor(rabbitmq)
            .WaitFor(blobs)
            .WaitForCompletion(aiMigrate);

        // The chat model's API key, the only secret not generated: set it once with
        // `dotnet user-secrets set Parameters:anthropic-api-key <key> --project src/core/Normetry.AppHost`.
        // Without it the AI service still starts and every answer is "unavailable", so a fresh
        // clone runs.
        if (!string.IsNullOrEmpty(builder.Configuration[$"Parameters:{AnthropicApiKey}"]))
        {
            ai.WithEnvironment(
                "NORMETRY_ANTHROPIC_API_KEY",
                builder.AddParameter(AnthropicApiKey, secret: true)
            );
        }
        return ai;
    }

    /// <summary>
    /// Every AI route, /health included, answers 401 without the internal token, and
    /// WithHttpHealthCheck cannot add headers, so this probe sends it.
    /// </summary>
    public static IResourceBuilder<UvicornAppResource> WithInternalTokenHealthCheck(
        this IResourceBuilder<UvicornAppResource> ai,
        IResourceBuilder<ParameterResource> internalToken
    )
    {
        var aiHttp = ai.GetEndpoint("http");
        var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        ai.ApplicationBuilder.Services.AddHealthChecks()
            .AddAsyncCheck(AiHealthCheck, async cancellationToken =>
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    new Uri(new Uri(aiHttp.Url), "/health")
                );
                request.Headers.Add(
                    HostingKeys.InternalTokenHeader,
                    await internalToken.Resource.GetValueAsync(cancellationToken)
                );
                using var response = await probe.SendAsync(request, cancellationToken);
                return response.IsSuccessStatusCode
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy($"/health returned {(int)response.StatusCode}");
            });
        return ai.WithHealthCheck(AiHealthCheck);
    }

    /// <summary>
    /// Probed over http so the API reports healthy even before the HTTPS dev certificate is
    /// trusted.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddCoreApi(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresServerResource> postgres,
        IResourceBuilder<ParameterResource> appCorePassword,
        IResourceBuilder<ParameterResource> coreDispatcherPassword,
        IResourceBuilder<ParameterResource> internalToken,
        IResourceBuilder<KeycloakResource> keycloak,
        IResourceBuilder<UvicornAppResource> ai,
        IResourceBuilder<RabbitMQServerResource> rabbitmq,
        IResourceBuilder<AzureBlobStorageResource> blobs,
        IResourceBuilder<ProjectResource> coreMigrate
    ) =>
        builder
            .AddProject<Projects.Normetry_Api>("api")
            .WithHttpHealthCheck("/health", endpointName: "http")
            .WithEnvironment(
                HostingKeys.ConnectionStringVariableOf(HostingKeys.CoreConnectionString),
                Npgsql(postgres, AppCoreRole, appCorePassword)
            )
            // Only the tenant list of the outbox dispatcher and the timeout sweeper:
            // core_dispatcher reads core.outbox_tenants and nothing else, so no request path can
            // list the tenants.
            .WithEnvironment(
                HostingKeys.ConnectionStringVariableOf(HostingKeys.DispatcherConnectionString),
                Npgsql(postgres, CoreDispatcherRole, coreDispatcherPassword)
            )
            .WithEnvironment(
                HostingKeys.EnvironmentVariableOf(HostingKeys.InternalTokenSetting),
                internalToken
            )
            .WithReference(keycloak)
            // services__ai__http__0 for the /ai/* gateway (Aspire service discovery).
            .WithReference(ai)
            // ConnectionStrings__rabbitmq (the outbox dispatcher and the status consumer) and
            // ConnectionStrings__blobs (uploads).
            .WithReference(rabbitmq)
            .WithReference(blobs)
            .WithEnvironment(
                HostingKeys.EnvironmentVariableOf(HostingKeys.BlobContainerSetting),
                DocumentsContainer
            )
            .WaitFor(keycloak)
            .WaitFor(postgres)
            .WaitFor(rabbitmq)
            .WaitFor(blobs)
            .WaitForCompletion(coreMigrate);

    /// <summary>
    /// The web app's Vite dev server. Fixed port: the normetry-web Keycloak client accepts only
    /// http://localhost:5173. The browser calls the API through the dev server's /api proxy, so the
    /// API needs no CORS; NORMETRY_API_URL is the proxy target (read by vite.config.ts).
    /// </summary>
    public static IResourceBuilder<ViteAppResource> AddWebApp(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> api,
        IResourceBuilder<KeycloakResource> keycloak
    ) =>
        builder
            .AddViteApp("web", WebDirectory)
            .WithNpm()
            .WithoutHttpsCertificate()
            .WithEndpoint("http", endpoint => endpoint.Port = 5173)
            .WithEnvironment("NORMETRY_API_URL", api.GetEndpoint("http"))
            .WithEnvironment(
                "VITE_OIDC_AUTHORITY",
                ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/{Realm}")
            )
            .WaitFor(api);

    private static ReferenceExpression Npgsql(
        IResourceBuilder<PostgresServerResource> postgres,
        string role,
        IResourceBuilder<ParameterResource> password
    )
    {
        var db = postgres.GetEndpoint("tcp");
        return ReferenceExpression.Create(
            $"Host={db.Property(EndpointProperty.Host)};Port={db.Property(EndpointProperty.Port)};Database={Database};Username={role};Password={password}"
        );
    }

    private static ReferenceExpression Psycopg(
        IResourceBuilder<PostgresServerResource> postgres,
        string role,
        IResourceBuilder<ParameterResource> password
    )
    {
        var db = postgres.GetEndpoint("tcp");
        return ReferenceExpression.Create(
            $"postgresql+psycopg://{role}:{password}@{db.Property(EndpointProperty.HostAndPort)}/{Database}"
        );
    }
}

/// <summary>The generated password of each role the Postgres bootstrap creates.</summary>
internal sealed record DatabasePasswords(
    IResourceBuilder<ParameterResource> CoreOwner,
    IResourceBuilder<ParameterResource> AiOwner,
    IResourceBuilder<ParameterResource> AppCore,
    IResourceBuilder<ParameterResource> CoreDispatcher,
    IResourceBuilder<ParameterResource> AppAi,
    IResourceBuilder<ParameterResource> CorpusIngestor
);
