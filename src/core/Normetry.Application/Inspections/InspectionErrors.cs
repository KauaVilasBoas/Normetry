using Normetry.BuildingBlocks.Results;

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

    public static readonly Error QuotaExceeded = new(
        "inspection_quota",
        "The organization has started all of today's inspections. The count resets at midnight UTC.",
        ErrorType.RateLimited
    );
}
