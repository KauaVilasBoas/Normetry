using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Documents.ListDocuments;

/// <summary>
/// The establishment's documents, most recently updated first. <paramref name="Expired"/> keeps
/// those whose expiry date is before today (UTC); <paramref name="ExpiringWithinDays"/> those
/// expiring from today to today plus that many days. At most one of the two.
/// </summary>
public sealed record ListDocumentsQuery(
    Guid EstablishmentId,
    bool Expired = false,
    int? ExpiringWithinDays = null
) : IQuery<IReadOnlyList<DocumentSummary>>;
