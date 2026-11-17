using Normetry.Application.Identity;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents;

/// <summary>A document with every version, newest first.</summary>
public sealed record DocumentResponse(
    Guid Id,
    Guid EstablishmentId,
    string Title,
    DocumentType Type,
    DateOnly? ExpiresOn,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? CurrentVersionNumber,
    IReadOnlyList<DocumentVersionResponse> Versions
)
{
    internal static DocumentResponse From(
        Document document,
        IReadOnlyDictionary<Guid, string?> uploaders
    ) =>
        new(
            document.Id,
            document.EstablishmentId,
            document.Title,
            document.Type,
            document.ExpiresOn,
            document.CreatedAt,
            document.UpdatedAt,
            document.Current?.Number,
            document
                .Versions.OrderByDescending(version => version.Number)
                .Select(version => DocumentVersionResponse.From(version, uploaders))
                .ToArray()
        );
}

/// <summary>A row of the list: the document and its newest version, which decides its status.</summary>
public sealed record DocumentSummary(
    Guid Id,
    Guid EstablishmentId,
    string Title,
    DocumentType Type,
    DateOnly? ExpiresOn,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? CurrentVersionNumber,
    DocumentVersionResponse LatestVersion
)
{
    internal static DocumentSummary From(
        Document document,
        IReadOnlyDictionary<Guid, string?> uploaders
    ) =>
        new(
            document.Id,
            document.EstablishmentId,
            document.Title,
            document.Type,
            document.ExpiresOn,
            document.CreatedAt,
            document.UpdatedAt,
            document.Current?.Number,
            DocumentVersionResponse.From(document.Newest, uploaders)
        );
}

/// <summary>
/// A version. <see cref="FailureCode"/> is one of the contract's reason codes or
/// <c>timed_out</c>; the web app shows its own text for it.
/// </summary>
public sealed record DocumentVersionResponse(
    Guid Id,
    int Number,
    string FileName,
    string ContentType,
    long Size,
    DocumentVersionStatus Status,
    string? FailureCode,
    string? FailureReason,
    UserReference UploadedBy,
    DateTimeOffset UploadedAt,
    DateTimeOffset StatusChangedAt,
    int? ChunkCount,
    int? PageCount,
    string? Language
)
{
    internal static DocumentVersionResponse From(
        DocumentVersion version,
        IReadOnlyDictionary<Guid, string?> uploaders
    ) =>
        new(
            version.Id,
            version.Number,
            version.FileName,
            version.ContentType,
            version.Size,
            version.Status,
            version.FailureCode,
            version.FailureReason,
            new UserReference(version.UploadedBy, uploaders.GetValueOrDefault(version.UploadedBy)),
            version.UploadedAt,
            version.StatusChangedAt,
            version.ChunkCount,
            version.PageCount,
            version.Language
        );
}
