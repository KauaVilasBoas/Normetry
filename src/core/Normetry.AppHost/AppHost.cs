using Normetry.AppHost;
using static Normetry.AppHost.AppHostConstants;

var builder = DistributedApplication.CreateBuilder(args);

var internalToken = builder.AddGeneratedSecret("internal-token");
var passwords = builder.AddDatabasePasswords();
var rabbitmqPassword = builder.AddGeneratedSecret("rabbitmq-password");

var postgres = builder.AddNormetryPostgres(passwords);

// Fixed port so the token issuer (http://localhost:8180/realms/normetry) is stable across runs.
// Plain http even when the dev certificate is trusted: Aspire would otherwise serve this endpoint
// over https and change the issuer the API (appsettings.json) and the web app expect.
var keycloak = builder
    .AddKeycloak("keycloak", port: 8180)
    .WithoutHttpsCertificate()
    .WithBindMount(
        $"{DeployDirectory}/keycloak/themes/normetry",
        "/opt/keycloak/themes/normetry",
        isReadOnly: true
    )
    .WithArgs(
        "--spi-theme--static-max-age=-1",
        "--spi-theme--cache-themes=false",
        "--spi-theme--cache-templates=false"
    )
    .WithRealmImport($"{DeployDirectory}/keycloak/normetry-realm.json");

// The queue between the core and the AI service (src/shared/contracts/messages). Kept small: the
// alpine image, no management plugin. The data volume keeps queued messages across runs, and a
// generated password that never changes, because the broker keeps its users in that volume.
var rabbitmq = builder
    .AddRabbitMQ("rabbitmq", password: rabbitmqPassword)
    .WithImageTag("4.3-alpine")
    .WithDataVolume();

// Azurite (Azure Blob Storage locally; the same SDK reaches Azure in production). The AppHost
// creates the documents container on startup; the data volume keeps the uploaded files, which
// core.document_versions points to.
var storage = builder.AddAzureStorage("storage").RunAsEmulator(azurite => azurite.WithDataVolume());
var blobs = storage.AddBlobs("blobs");
storage.AddBlobContainer("documents-container", DocumentsContainer);

var coreMigrate = builder.AddCoreMigrations(postgres, passwords.CoreOwner);
var aiMigrate = builder.AddAiMigrations(postgres, passwords.AiOwner);
builder.AddCorpusIngest(postgres, passwords.CorpusIngestor, aiMigrate);
var ai = builder
    .AddAiService(postgres, passwords.AppAi, internalToken, rabbitmq, blobs, aiMigrate)
    .WithInternalTokenHealthCheck(internalToken);
var api = builder.AddCoreApi(
    postgres,
    passwords.AppCore,
    passwords.CoreDispatcher,
    internalToken,
    keycloak,
    ai,
    rabbitmq,
    blobs,
    coreMigrate
);
builder.AddWebApp(api, keycloak);

builder.Build().Run();
