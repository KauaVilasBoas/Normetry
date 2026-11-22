using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.ReviewFinding;

/// <summary>
/// A reviewer's decision on a finding of a completed inspection of the establishment; the latest
/// decision wins. A rejection needs <see cref="Note"/>. An edit changes the fields it gives (codes
/// are the contract's snake_case ones) and keeps the inspection's values on the first edit; for a
/// finding with no action, <see cref="ActionText"/> and <see cref="DueOn"/> come together and
/// create one. Returns the finding as stored.
/// </summary>
public sealed record ReviewFindingCommand(
    Guid EstablishmentId,
    Guid InspectionId,
    Guid FindingId,
    ReviewDecision Decision,
    string? Note = null,
    string? Verdict = null,
    string? Severity = null,
    string? Rationale = null,
    string? ActionText = null,
    DateOnly? DueOn = null
) : ICommand<FindingResponse>
{
    /// <summary>The fields an <see cref="ReviewDecision.Edit"/> changes.</summary>
    public FindingEdit ToEdit() => new(Verdict, Severity, Rationale, ActionText, DueOn);
}

public enum ReviewDecision
{
    Accept = 1,
    Reject,
    Edit,
}
