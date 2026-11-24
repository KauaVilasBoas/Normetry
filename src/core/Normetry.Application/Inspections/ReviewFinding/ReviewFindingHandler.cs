using Normetry.Application.Abstractions;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.ReviewFinding;

internal sealed class ReviewFindingHandler(
    FindingReviewService reviews,
    IUserRepository users,
    ICurrentUser currentUser,
    TimeProvider clock
) : ICommandHandler<ReviewFindingCommand, FindingResponse>
{
    public async Task<Result<FindingResponse>> HandleAsync(
        ReviewFindingCommand request,
        CancellationToken cancellationToken
    )
    {
        var found = await FindFindingOfCompletedInspectionAsync(request, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error;
        }
        var finding = found.Value;
        var refusal = RefuseNewActionWithoutBothTextAndDueOn(request, finding);
        if (refusal is not null)
        {
            return refusal;
        }
        var reviewer = await users.CallerIdAsync(currentUser, cancellationToken);
        ApplyReviewDecision(request, finding, reviewer, clock.GetUtcNow());
        return await reviews.ResponseAsync(finding, cancellationToken);
    }

    /// <summary>The finding the route names, in its Completed inspection, tracked and locked.</summary>
    private async Task<Result<Finding>> FindFindingOfCompletedInspectionAsync(
        ReviewFindingCommand request,
        CancellationToken cancellationToken
    )
    {
        var loaded = await reviews.CompletedInspectionAsync(
            request.EstablishmentId,
            request.InspectionId,
            InspectionErrors.FindingNotFound,
            cancellationToken
        );
        if (!loaded.IsSuccess)
        {
            return loaded.Error;
        }
        return
            loaded.Value.Findings.FirstOrDefault(finding => finding.Id == request.FindingId)
                is { } finding
            ? finding
            : InspectionErrors.FindingNotFound;
    }

    /// <summary>
    /// An edit creating a finding's action must give both its text and due date. The domain
    /// refuses this too, but by throwing: here it is the caller's input.
    /// </summary>
    private static Error? RefuseNewActionWithoutBothTextAndDueOn(
        ReviewFindingCommand request,
        Finding finding
    ) =>
        request.Decision == ReviewDecision.Edit && finding.NeedsActionTextAndDueOn(request.ToEdit())
            ? InspectionErrors.ActionNeedsTextAndDueOn
            : null;

    private static void ApplyReviewDecision(
        ReviewFindingCommand request,
        Finding finding,
        Guid reviewer,
        DateTimeOffset now
    )
    {
        switch (request.Decision)
        {
            case ReviewDecision.Accept:
                finding.Accept(reviewer, now);
                break;
            case ReviewDecision.Reject:
                finding.Reject(reviewer, request.Note!, now);
                break;
            case ReviewDecision.Edit:
                finding.Edit(reviewer, request.ToEdit(), now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Decision, null);
        }
    }
}
