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
// Plain http even when the dev certificate is trusted: Aspire would otherwise serve this endpoint
// over https and change the issuer the API (appsettings.json) and the web app expect.
var keycloak = builder
    .AddKeycloak("keycloak", port: 8180)
    .WithoutHttpsCertificate()
    .WithRealmImport("../../../deploy/keycloak/normetry-realm.json");

// Short-lived: applies the core migrations as core_owner and exits. Only this step gets the owner
// connection string, because the owner can switch RLS off; the API connects as app_core.
var coreMigrate = builder
    .AddProject<Projects.Normetry_Migrations>("core-migrate")
    .WithEnvironment("ConnectionStrings__core-migrations", Npgsql("core_owner", coreOwnerPassword))
    .WaitFor(postgres);

// Short-lived: applies the ai migrations as ai_owner and exits. Only this step gets the owner URL,
// because the owner can switch RLS off; the running service connects as app_ai.
var aiMigrate = builder
    .AddExecutable("ai-migrate", "uv", "../../ai", "run", "alembic", "upgrade", "head")
    .WithEnvironment("NORMETRY_MIGRATIONS_DATABASE_URL", Psycopg("ai_owner", aiOwnerPassword))
    .WaitFor(postgres);

// On demand (start it from the dashboard): loads corpus/manifest.json into ai.sources and ai.chunks.
// corpus_ingestor is the only role that writes the public corpus, so only this step gets its URL.
// Fetch the corpus first (corpus/scripts/fetch_corpus.py); the first run also downloads the
// embedding model (about 2.3 GB) into the Hugging Face cache.
builder
    .AddExecutable(
        "corpus-ingest",
        "uv",
        "../../ai",
        "run",
        "python",
        "-m",
        "normetry_ai.ingestion.ingest"
    )
    .WithEnvironment(
        "NORMETRY_CORPUS_DATABASE_URL",
        Psycopg("corpus_ingestor", corpusIngestorPassword)
    )
    .WithExplicitStart()
    .WaitFor(postgres)
    .WaitForCompletion(aiMigrate);

var ai = builder
    .AddUvicornApp("ai", "../../ai", "normetry_ai.api:create_app")
    .WithUv()
    // Plain http on loopback, so the API gets services__ai__http__0 even with a trusted dev cert.
    .WithoutHttpsCertificate()
    // AddUvicornApp adds --reload; without --reload-dir it would poll every file under .venv too.
    .WithArgs("--factory", "--reload-dir", "normetry_ai")
    .WithEnvironment("NORMETRY_INTERNAL_TOKEN", internalToken)
    .WithEnvironment("NORMETRY_DATABASE_URL", Psycopg("app_ai", appAiPassword))
    .WaitFor(postgres)
    .WaitForCompletion(aiMigrate);

// The chat model's API key, the only secret not generated: set it once with
// `dotnet user-secrets set Parameters:anthropic-api-key <key> --project src/core/Normetry.AppHost`.
// Without it the AI service still starts and every answer is "unavailable", so a fresh clone runs.
if (!string.IsNullOrEmpty(builder.Configuration["Parameters:anthropic-api-key"]))
{
    ai.WithEnvironment(
        "NORMETRY_ANTHROPIC_API_KEY",
        builder.AddParameter("anthropic-api-key", secret: true)
    );
}

// Probed over http so the API reports healthy even before the HTTPS dev certificate is trusted.
var api = builder
    .AddProject<Projects.Normetry_Api>("api")
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithEnvironment("ConnectionStrings__core", Npgsql("app_core", appCorePassword))
    .WithEnvironment("Ai__InternalToken", internalToken)
    .WithReference(keycloak)
    // services__ai__http__0 for the /ai/* gateway (Aspire service discovery).
    .WithReference(ai)
    .WaitFor(keycloak)
    .WaitFor(postgres)
    .WaitForCompletion(coreMigrate);

// The web app's Vite dev server. Fixed port: the normetry-web Keycloak client accepts only
// http://localhost:5173. The browser calls the API through the dev server's /api proxy, so the
// API needs no CORS; NORMETRY_API_URL is the proxy target (read by vite.config.ts).
builder
    .AddViteApp("web", "../../web")
    .WithNpm()
    .WithoutHttpsCertificate()
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithEnvironment("NORMETRY_API_URL", api.GetEndpoint("http"))
    .WithEnvironment(
        "VITE_OIDC_AUTHORITY",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/normetry")
    )
    .WaitFor(api);

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
