using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace Normetry.AppHost;

/// <summary>
/// The AI service's blob credential. It only downloads uploads (<c>BlobStore</c> in src/ai), so it
/// gets a service SAS for one container with read permission only: no write, create, delete, list
/// or other container. Made from the account key every time the AI service starts and kept in
/// memory, never on disk or in a log; only the API holds the key.
/// </summary>
internal static class ReadOnlyBlobAccess
{
    /// <summary>
    /// A connection string both storage SDKs read: <c>BlobEndpoint=...;SharedAccessSignature=...</c>.
    /// </summary>
    public static string ConnectionString(
        string accountConnectionString,
        string container,
        DateTimeOffset expiresOn
    )
    {
        var account = new BlobServiceClient(accountConnectionString);
        var sas = account
            .GetBlobContainerClient(container)
            .GenerateSasUri(BlobContainerSasPermissions.Read, expiresOn);
        return $"BlobEndpoint={account.Uri};SharedAccessSignature={sas.Query.TrimStart('?')}";
    }
}
