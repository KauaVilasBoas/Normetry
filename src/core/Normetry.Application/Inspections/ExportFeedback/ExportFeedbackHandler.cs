using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.ExportFeedback;

internal sealed class ExportFeedbackHandler(
    IInspectionRepository inspections,
    IUserRepository users
) : IQueryHandler<ExportFeedbackQuery, IReadOnlyList<FeedbackExample>>
{
    public async Task<Result<IReadOnlyList<FeedbackExample>>> HandleAsync(
        ExportFeedbackQuery request,
        CancellationToken cancellationToken
    )
    {
        // RLS hides another tenant's inspection; another establishment's is not found either.
        if (
            await inspections.GetAsync(request.InspectionId, cancellationToken)
                is not { } inspection
            || inspection.EstablishmentId != request.EstablishmentId
        )
        {
            return InspectionErrors.NotFound;
        }
        // An edited finding keeps its original after a later accept, so the correction still counts.
        var reviewed = inspection
            .Findings.Where(finding =>
                finding.ReviewStatus is ReviewStatus.Rejected || finding.Original is not null
            )
            .OrderBy(finding => finding.ItemId, StringComparer.Ordinal)
            .ToArray();
        var subs = await users.GetSubsAsync(
            reviewed.Select(finding => finding.ReviewedBy!.Value).Distinct(),
            cancellationToken
        );
        return reviewed
            .Select(finding => new FeedbackExample(
                finding.ItemId,
                finding.Original ?? Current(finding),
                Current(finding),
                finding.ReviewNote,
                subs.GetValueOrDefault(finding.ReviewedBy!.Value)
            ))
            .ToArray();
    }

    private static FindingOriginal Current(Finding finding) =>
        new(
            finding.Verdict,
            finding.Severity,
            finding.Rationale,
            finding.ActionItem?.Text,
            finding.ActionItem?.DueOn
        );
}
