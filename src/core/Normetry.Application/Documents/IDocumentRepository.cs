using Normetry.Domain.Documents;

namespace Normetry.Application.Documents;

/// <summary>
/// Documents of the current tenant, with their versions. Row-level security scopes every call to
/// the tenant of the request's transaction. Changes are saved when the transaction commits.
/// </summary>
public interface IDocumentRepository
{
    void Add(Document document);

    /// <summary>
    /// Tracked, with its versions, deleted or not, and locked until the transaction ends: uploads,
    /// deletes and status events of one document run one at a time.
    /// </summary>
    Task<Document?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Not deleted, with its versions.</summary>
    Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The establishment's documents that are not deleted, with their versions.</summary>
    Task<IReadOnlyList<Document>> ListAsync(
        Guid establishmentId,
        CancellationToken cancellationToken
    );

    /// <summary>The establishment's documents that are not deleted.</summary>
    Task<int> CountAsync(Guid establishmentId, CancellationToken cancellationToken);

    /// <summary>A document of the establishment that is not deleted has a current (indexed) version.</summary>
    Task<bool> HasIndexedAsync(Guid establishmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Documents with a version Processing since before <paramref name="before"/>, tracked with
    /// their versions and locked; documents another transaction holds are skipped.
    /// </summary>
    Task<IReadOnlyList<Document>> ListProcessingSinceForUpdateAsync(
        DateTimeOffset before,
        CancellationToken cancellationToken
    );
}

/// <summary>The files of document versions (Azure Blob Storage; Azurite locally).</summary>
public interface IDocumentBlobStore
{
    /// <summary>Stores a new blob at <paramref name="path"/>; an existing one is never overwritten.</summary>
    Task UploadAsync(
        string path,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken
    );
}
