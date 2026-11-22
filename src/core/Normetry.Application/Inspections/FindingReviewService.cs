using Normetry.Application.Abstractions;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections;

/// <summary>
/// What the review of a finding and the status change of its action share. Public only so the
/// composition root can register it; the handlers are its only callers.
/// </summary>
public sealed class FindingReviewService(
    IInspectionRepository inspections,
    IUserRepository users,
    ICurrentUser currentUser
)
{
    /// <summary>
    /// The inspection, tracked and locked, when it is one of the establishment's (RLS hides another
    /// tenant's) and Completed. <paramref name="notFound"/> is what the route names.
    /// </summary>
    internal async Task<Result<Inspection>> CompletedInspectionAsync(
        Guid establishmentId,
        Guid inspectionId,
        Error notFound,
        CancellationToken cancellationToken
    )
    {
        if (
            await inspections.GetForReviewAsync(inspectionId, cancellationToken)
                is not { } inspection
            || inspection.EstablishmentId != establishmentId
        )
        {
            return notFound;
        }
        // Findings arrive with the completion, so this guards a future status that keeps them.
        return inspection.Status == InspectionStatus.Completed
            ? inspection
            : InspectionErrors.NotCompleted;
    }

    /// <summary>The caller's user id. They hold a permission, so their user row exists.</summary>
    internal async Task<Guid> ReviewerIdAsync(CancellationToken cancellationToken) =>
        await users.FindIdAsync(currentUser.UserId, cancellationToken)
        ?? throw new InvalidOperationException("The caller has no user row.");

    /// <summary>The finding as <c>GET</c> returns it, with its reviewer's and updater's names.</summary>
    internal async Task<FindingResponse> ResponseAsync(
        Finding finding,
        CancellationToken cancellationToken
    ) =>
        FindingResponse.From(
            finding,
            await users.GetDisplayNamesAsync(FindingResponse.UserIds(finding), cancellationToken)
        );
}
