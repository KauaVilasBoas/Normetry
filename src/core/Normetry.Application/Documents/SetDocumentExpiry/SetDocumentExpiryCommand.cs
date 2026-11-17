using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Documents.SetDocumentExpiry;

/// <summary>
/// Sets or, with <c>null</c>, clears the expiry date of a document of the establishment. A past
/// date is allowed. A deleted document, or one of another establishment, is not found.
/// </summary>
public sealed record SetDocumentExpiryCommand(
    Guid EstablishmentId,
    Guid DocumentId,
    DateOnly? ExpiresOn
) : ICommand;
