using Normetry.Application.Inspections.ReviewFinding;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections;

/// <summary>
/// The stable codes of the inspection refusals. The web app picks its notice by these codes
/// (<c>docs/design/inspection-screen.md</c>), so they are the contract's, not dotted.
/// </summary>
public static class InspectionErrors
{
    // Another tenant's inspection is invisible under row-level security, so it is not found either.
    public static readonly Error NotFound = new(
        "inspection.not_found",
        "The inspection was not found.",
        ErrorType.NotFound
    );

    public static readonly Error Running = new(
        "inspection_running",
        "An inspection of this establishment is already running. Start another when it finishes.",
        ErrorType.Conflict
    );

    public static readonly Error NoIndexedDocuments = new(
        "no_indexed_documents",
        "The establishment has no indexed document yet. An inspection checks its own documents.",
        ErrorType.Conflict
    );

    public static readonly Error Capacity = new(
        "inspection_capacity",
        "The organization has as many inspections running as it may at once. Start another when one finishes.",
        ErrorType.Conflict
    );

    public static readonly Error QuotaExceeded = new(
        "inspection_quota",
        "The organization has started all of today's inspections. The count resets at midnight UTC.",
        ErrorType.RateLimited
    );

    // A finding of another tenant, inspection or establishment is not found either.
    public static readonly Error FindingNotFound = new(
        "finding.not_found",
        "The finding was not found.",
        ErrorType.NotFound
    );

    public static readonly Error ActionItemNotFound = new(
        "action_item.not_found",
        "The action item was not found.",
        ErrorType.NotFound
    );

    public static readonly Error NotCompleted = new(
        "inspection_not_completed",
        "Only a completed inspection's findings can be reviewed.",
        ErrorType.Conflict
    );

    /// <summary>An edit gave only one of the two for a finding that has no action.</summary>
    public static readonly Error ActionNeedsTextAndDueOn = Error.Validation(
        new Dictionary<string, string[]>
        {
            [nameof(ReviewFindingCommand.ActionText)] = [Finding.ActionNeedsTextAndDueOnMessage],
            [nameof(ReviewFindingCommand.DueOn)] = [Finding.ActionNeedsTextAndDueOnMessage],
        }
    );
}
