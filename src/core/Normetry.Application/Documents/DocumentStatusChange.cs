using Normetry.Domain.Documents;

namespace Normetry.Application.Documents;

/// <summary>
/// A version's new status, pushed to the establishment's web clients once the change is
/// committed. <see cref="CurrentVersionNumber"/> is the document's newest indexed version.
/// </summary>
public sealed record DocumentStatusChange(
    Guid EstablishmentId,
    Guid DocumentId,
    Guid VersionId,
    int VersionNumber,
    DocumentVersionStatus Status,
    string? FailureCode,
    string? FailureReason,
    int? CurrentVersionNumber,
    DateTimeOffset ChangedAt
)
{
    internal static DocumentStatusChange From(Document document, DocumentVersion version) =>
        new(
            document.EstablishmentId,
            document.Id,
            version.Id,
            version.Number,
            version.Status,
            version.FailureCode,
            version.FailureReason,
            document.Current?.Number,
            version.StatusChangedAt
        );
}
