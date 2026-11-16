using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents.UploadDocument;

/// <summary>
/// Creates a document of the establishment with its first version, stores the file and announces
/// it to the AI service. Returns the document as stored, with the version Uploaded.
/// </summary>
public sealed record UploadDocumentCommand(
    Guid EstablishmentId,
    string Title,
    DocumentType Type,
    UploadedFile? File
) : ICommand<DocumentResponse>;
