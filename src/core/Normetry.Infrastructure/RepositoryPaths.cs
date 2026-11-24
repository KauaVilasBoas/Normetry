using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Normetry.Infrastructure;

/// <summary>Development folders of this repository that a setting can move.</summary>
public static class RepositoryPaths
{
    /// <summary>
    /// The full path of <paramref name="setting"/>'s value when set, otherwise of
    /// <paramref name="fromRepositoryRoot"/> under the repository root, three levels above the
    /// API's content root (<c>src/core/Normetry.Api</c>).
    /// </summary>
    public static string Resolve(
        IConfiguration configuration,
        string setting,
        IHostEnvironment environment,
        params string[] fromRepositoryRoot
    ) =>
        Path.GetFullPath(
            configuration[setting]
                ?? Path.Combine([
                    environment.ContentRootPath,
                    "..",
                    "..",
                    "..",
                    .. fromRepositoryRoot,
                ])
        );
}
