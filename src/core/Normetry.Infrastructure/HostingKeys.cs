using Microsoft.Extensions.Configuration;

namespace Normetry.Infrastructure;

/// <summary>
/// Names the AppHost sets and the core reads. This file is compiled into Normetry.Infrastructure
/// and linked into Normetry.AppHost, whose project references do not reference the assemblies, so
/// each name is spelled once.
/// </summary>
public static class HostingKeys
{
    /// <summary>The API's connection string: the <c>app_core</c> role.</summary>
    public const string CoreConnectionString = "core";

    /// <summary>The outbox dispatcher's and the sweeper's tenant list: <c>core_dispatcher</c>.</summary>
    public const string DispatcherConnectionString = "core-dispatcher";

    /// <summary>The migrator's only connection string: the <c>core_owner</c> role.</summary>
    public const string MigrationsConnectionString = "core-migrations";

    /// <summary>The token the gateway sends the AI service.</summary>
    public const string InternalTokenSetting = "Ai:InternalToken";

    /// <summary>The header that carries <see cref="InternalTokenSetting"/>.</summary>
    public const string InternalTokenHeader = "X-Normetry-Internal-Token";

    /// <summary>The blob container of the uploaded documents.</summary>
    public const string BlobContainerSetting = "Documents:BlobContainer";

    /// <summary>The AI service's name in Aspire service discovery.</summary>
    public const string AiService = "ai";

    private const string ConnectionStringsSection = "ConnectionStrings";

    /// <summary>What the environment variable configuration provider reads as <c>:</c>.</summary>
    private const string EnvironmentKeyDelimiter = "__";

    /// <summary>
    /// The environment variable that sets a setting: <c>Ai__InternalToken</c> for
    /// <c>Ai:InternalToken</c>.
    /// </summary>
    public static string EnvironmentVariableOf(string setting) =>
        setting.Replace(
            ConfigurationPath.KeyDelimiter,
            EnvironmentKeyDelimiter,
            StringComparison.Ordinal
        );

    /// <summary>
    /// The environment variable that sets a connection string: <c>ConnectionStrings__core</c> for
    /// <c>core</c>.
    /// </summary>
    public static string ConnectionStringVariableOf(string name) =>
        EnvironmentVariableOf(ConfigurationPath.Combine(ConnectionStringsSection, name));
}
