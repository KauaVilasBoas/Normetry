using Normetry.Application.Abstractions;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections;

/// <summary>What the review of a finding and the status change of its action share.</summary>
internal static class FindingReviews
{
    /// <summary>
    /// The inspection, tracked and locked, when it is one of the establishment's (RLS hides another
    /// tenant's) and Completed. <paramref name="notFound"/> is what the route names.
    /// </summary>
    public static async Task<Result<Inspection>> CompletedInspectionAsync(
        IInspectionRepository inspections,
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
    public static async Task<Guid> ReviewerAsync(
        IUserRepository users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken
    ) =>
        await users.FindIdAsync(currentUser.UserId, cancellationToken)
        ?? throw new InvalidOperationException("The caller has no user row.");

    /// <summary>The finding as <c>GET</c> returns it, with its reviewer's and updater's names.</summary>
    public static async Task<FindingResponse> ResponseAsync(
        IUserRepository users,
        Finding finding,
        CancellationToken cancellationToken
    ) =>
        FindingResponse.From(
            finding,
            await users.GetDisplayNamesAsync(
                new[] { finding.ReviewedBy, finding.ActionItem?.UpdatedBy }
                    .OfType<Guid>()
                    .Distinct(),
                cancellationToken
            )
        );
}
