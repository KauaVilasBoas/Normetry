using System.Collections.Concurrent;
using Normetry.Application.Documents;

namespace Normetry.IntegrationTests.Documents;

/// <summary>The blob store of the in-memory API: what was stored, by path.</summary>
public sealed class InMemoryBlobStore : IDocumentBlobStore
{
    public ConcurrentDictionary<string, (byte[] Content, string ContentType)> Blobs { get; } =
        new();

    public Task UploadAsync(
        string path,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken
    ) =>
        Blobs.TryAdd(path, (content, contentType))
            ? Task.CompletedTask
            : throw new InvalidOperationException($"A blob already exists at {path}.");
}
