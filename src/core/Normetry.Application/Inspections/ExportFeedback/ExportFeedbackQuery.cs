using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.ExportFeedback;

/// <summary>
/// The rejected and edited findings of an inspection of the establishment, as eval examples,
/// ordered by item id. Development only: the API's export writes them to
/// <c>evals/datasets/inspection-feedback/</c>.
/// </summary>
public sealed record ExportFeedbackQuery(Guid EstablishmentId, Guid InspectionId)
    : IQuery<IReadOnlyList<FeedbackExample>>;

/// <summary>
/// One reviewed finding: the inspection's values, the current ones (the same for a rejection
/// without an edit), the rejection note (null for an edit) and the reviewer's <c>sub</c>.
/// <see cref="FindingOriginal"/> is reused for both so their keys always match.
/// </summary>
public sealed record FeedbackExample(
    string ItemId,
    FindingOriginal Original,
    FindingOriginal Correction,
    string? Note,
    string? Reviewer
);
