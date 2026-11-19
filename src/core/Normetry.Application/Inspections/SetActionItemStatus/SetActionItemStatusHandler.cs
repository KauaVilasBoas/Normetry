using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.SetActionItemStatus;

internal sealed class SetActionItemStatusHandler(FindingReviewService reviews, TimeProvider clock)
    : ICommandHandler<SetActionItemStatusCommand, FindingResponse>
{
    public async Task<Result<FindingResponse>> HandleAsync(
        SetActionItemStatusCommand request,
        CancellationToken cancellationToken
    )
    {
        var loaded = await reviews.CompletedInspectionAsync(
            request.EstablishmentId,
            request.InspectionId,
            InspectionErrors.ActionItemNotFound,
            cancellationToken
        );
        if (!loaded.IsSuccess)
        {
            return loaded.Error;
        }
        if (
            loaded.Value.Findings.FirstOrDefault(finding =>
                finding.ActionItem?.Id == request.ActionItemId
            )
            is not { ActionItem: { } item } finding
        )
        {
            return InspectionErrors.ActionItemNotFound;
        }

        var user = await reviews.ReviewerIdAsync(cancellationToken);
        if (request.Status == ActionItemStatus.Done)
        {
            item.MarkDone(user, clock.GetUtcNow());
        }
        else
        {
            item.Reopen(user, clock.GetUtcNow());
        }
        return await reviews.ResponseAsync(finding, cancellationToken);
    }
}
