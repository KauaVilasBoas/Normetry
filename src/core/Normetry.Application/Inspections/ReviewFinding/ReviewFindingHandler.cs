using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.ReviewFinding;

internal sealed class ReviewFindingHandler(FindingReviewService reviews, TimeProvider clock)
    : ICommandHandler<ReviewFindingCommand, FindingResponse>
{
    public async Task<Result<FindingResponse>> HandleAsync(
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
        if (
            loaded.Value.Findings.FirstOrDefault(finding => finding.Id == request.FindingId)
            is not { } finding
        )
        {
            return InspectionErrors.FindingNotFound;
        }
        // The domain refuses this too, but by throwing: here it is the caller's input.
        if (
            request.Decision == ReviewDecision.Edit
            && finding.ActionItem is null
            && (request.ActionText is null) != (request.DueOn is null)
        )
        {
            return InspectionErrors.ActionNeedsTextAndDueOn;
        }

        var reviewer = await reviews.ReviewerIdAsync(cancellationToken);
        var now = clock.GetUtcNow();
        switch (request.Decision)
        {
            case ReviewDecision.Accept:
                finding.Accept(reviewer, now);
                break;
            case ReviewDecision.Reject:
                finding.Reject(reviewer, request.Note!, now);
                break;
            case ReviewDecision.Edit:
                finding.Edit(
                    reviewer,
                    new FindingEdit(
                        request.Verdict,
                        request.Severity,
                        request.Rationale,
                        request.ActionText,
                        request.DueOn
                    ),
                    now
                );
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Decision, null);
        }
        return await reviews.ResponseAsync(finding, cancellationToken);
    }
}
