using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Documents.UploadDocumentVersion;

/// <summary>
/// Adds the next version of a document, stores the file and announces it to the AI service.
/// Refused while the newest version is Uploaded or Processing. Returns the document as stored.
/// </summary>
public sealed record UploadDocumentVersionCommand(Guid DocumentId, UploadedFile? File)
    : ICommand<DocumentResponse>;
