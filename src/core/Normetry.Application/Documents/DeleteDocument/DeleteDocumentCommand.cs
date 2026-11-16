using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Documents.DeleteDocument;

/// <summary>
/// Soft-deletes a document and tells the AI service, which removes its chunks. Its rows and
/// blobs stay.
/// </summary>
public sealed record DeleteDocumentCommand(Guid Id) : ICommand;
