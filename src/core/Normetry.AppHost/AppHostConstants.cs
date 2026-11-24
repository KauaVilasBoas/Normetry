namespace Normetry.AppHost;

/// <summary>
/// The folders the AppHost starts or mounts, relative to this project, and the names the
/// bootstrap in <c>deploy/</c> creates.
/// </summary>
internal static class AppHostConstants
{
    /// <summary>The repository's <c>deploy/</c> folder: the Postgres bootstrap and the Keycloak realm.</summary>
    public const string DeployDirectory = "../../../deploy";

    /// <summary>The Python AI service, <c>src/ai</c>.</summary>
    public const string AiDirectory = "../../ai";

    /// <summary>The web app, <c>src/web</c>.</summary>
    public const string WebDirectory = "../../web";

    /// <summary>The one database, created by <c>deploy/postgres/init</c>.</summary>
    public const string Database = "normetry";

    // The roles deploy/postgres/init creates (deploy/postgres/README.md).
    public const string CoreOwnerRole = "core_owner";
    public const string AiOwnerRole = "ai_owner";
    public const string AppCoreRole = "app_core";
    public const string CoreDispatcherRole = "core_dispatcher";
    public const string AppAiRole = "app_ai";
    public const string CorpusIngestorRole = "corpus_ingestor";

    /// <summary>The Keycloak realm in <c>deploy/keycloak/normetry-realm.json</c>.</summary>
    public const string Realm = "normetry";

    /// <summary>The blob container the AppHost creates for the uploaded documents.</summary>
    public const string DocumentsContainer = "documents";
}
