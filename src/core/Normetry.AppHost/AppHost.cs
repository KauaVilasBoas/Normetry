using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = DistributedApplication.CreateBuilder(args);

// Generated on the first run and kept in the AppHost user secrets, so a fresh clone needs no setup.
// Letters and digits only: the values go unescaped into connection strings and URLs.
IResourceBuilder<ParameterResource> Secret(string name) =>
    builder.AddParameter(
        name,
        new GenerateParameterDefault { MinLength = 32, Special = false },
        secret: true,
        persist: true
    );

var internalToken = Secret("internal-token");
var coreOwnerPassword = Secret("core-owner-password");
var aiOwnerPassword = Secret("ai-owner-password");
var appCorePassword = Secret("app-core-password");
var appAiPassword = Secret("app-ai-password");
var corpusIngestorPassword = Secret("corpus-ingestor-password");

// The init script runs only on an empty data volume: it creates the roles and both schemas.
var postgres = builder
    .AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "0.8.7-pg17")
    .WithDataVolume()
    .WithInitFiles("../../../deploy/postgres/init")
    .WithEnvironment("POSTGRES_DB", "normetry")
    .WithEnvironment("CORE_OWNER_PASSWORD", coreOwnerPassword)
    .WithEnvironment("AI_OWNER_PASSWORD", aiOwnerPassword)
    .WithEnvironment("APP_CORE_PASSWORD", appCorePassword)
    .WithEnvironment("APP_AI_PASSWORD", appAiPassword)
    .WithEnvironment("CORPUS_INGESTOR_PASSWORD", corpusIngestorPassword);

var db = postgres.GetEndpoint("tcp");

ReferenceExpression Npgsql(string user, IResourceBuilder<ParameterResource> password) =>
    ReferenceExpression.Create(
        $"Host={db.Property(EndpointProperty.Host)};Port={db.Property(EndpointProperty.Port)};Database=normetry;Username={user};Password={password}"
    );

ReferenceExpression Psycopg(string user, IResourceBuilder<ParameterResource> password) =>
    ReferenceExpression.Create(
        $"postgresql+psycopg://{user}:{password}@{db.Property(EndpointProperty.HostAndPort)}/normetry"
    );

// Fixed port so the token issuer (http://localhost:8180/realms/normetry) is stable across runs.
var keycloak = builder
    .AddKeycloak("keycloak", port: 8180)
    .WithRealmImport("../../../deploy/keycloak/normetry-realm.json");

// Probed over http so the API reports healthy even before the HTTPS dev certificate is trusted.
builder
    .AddProject<Projects.Normetry_Api>("api")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithEnvironment("ConnectionStrings__core", Npgsql("app_core", appCorePassword))
    .WithEnvironment("Ai__InternalToken", internalToken)
    .WithReference(keycloak)
    .WaitFor(postgres);

var ai = builder
    .AddUvicornApp("ai", "../../ai", "normetry_ai.api:create_app")
    .WithUv()
    // AddUvicornApp adds --reload; without --reload-dir it would poll every file under .venv too.
    .WithArgs("--factory", "--reload-dir", "normetry_ai")
    .WithEnvironment("NORMETRY_INTERNAL_TOKEN", internalToken)
    .WithEnvironment("NORMETRY_DATABASE_URL", Psycopg("app_ai", appAiPassword))
    .WaitFor(postgres);

// Every AI route, /health included, answers 401 without the internal token, and
// WithHttpHealthCheck cannot add headers, so this probe sends it.
var aiHttp = ai.GetEndpoint("http");
var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
builder
    .Services.AddHealthChecks()
    .AddAsyncCheck("ai-health", async cancellationToken =>
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(aiHttp.Url), "/health")
        );
        request.Headers.Add(
            "X-Normetry-Internal-Token",
            await internalToken.Resource.GetValueAsync(cancellationToken)
        );
        using var response = await probe.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"/health returned {(int)response.StatusCode}");
    });
ai.WithHealthCheck("ai-health");

builder.Build().Run();
