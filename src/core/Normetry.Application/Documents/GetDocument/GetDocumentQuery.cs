using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Documents.GetDocument;

/// <summary>A document that is not deleted, with every version.</summary>
public sealed record GetDocumentQuery(Guid Id) : IQuery<DocumentResponse>;
