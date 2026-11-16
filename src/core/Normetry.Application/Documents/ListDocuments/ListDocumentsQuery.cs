using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Documents.ListDocuments;

/// <summary>The establishment's documents, most recently updated first.</summary>
public sealed record ListDocumentsQuery(Guid EstablishmentId)
    : IQuery<IReadOnlyList<DocumentSummary>>;
