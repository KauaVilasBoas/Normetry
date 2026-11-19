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
        var found = await FindActionItemOfCompletedInspectionAsync(request, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error;
        }
        var (finding, item) = found.Value;
        var user = await reviews.ReviewerIdAsync(cancellationToken);
        ApplyStatus(request.Status, item, user, clock.GetUtcNow());
        return await reviews.ResponseAsync(finding, cancellationToken);
    }

    /// <summary>
    /// The action item the route names and its finding, in their Completed inspection, tracked and
    /// locked.
    /// </summary>
    private async Task<
        Result<(Finding Finding, ActionItem Item)>
    > FindActionItemOfCompletedInspectionAsync(
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
        return (finding, item);
    }

    private static void ApplyStatus(
        ActionItemStatus status,
        ActionItem item,
        Guid user,
        DateTimeOffset now
    )
    {
        if (status == ActionItemStatus.Done)
        {
            item.MarkDone(user, now);
        }
        else
        {
            item.Reopen(user, now);
        }
    }
}
