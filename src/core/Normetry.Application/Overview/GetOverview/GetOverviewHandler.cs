using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Establishments;
using Normetry.Application.Identity;
using Normetry.Application.Inspections;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Identity;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Overview.GetOverview;

internal sealed class GetOverviewHandler(
    IEstablishmentRepository establishments,
    IUserRepository users,
    ICurrentUser currentUser,
    IInspectionRepository inspections,
    IDocumentRepository documents,
    TimeProvider clock
) : IQueryHandler<GetOverviewQuery, OverviewResponse>
{
    private const int SeriesLength = 12;
    private const int ExpiringWithinDays = 30;
    private const int DocumentListLength = 10;

    private static readonly string[] ThemeOrder = [.. ThemeProgress.InOrder];

    public async Task<Result<OverviewResponse>> HandleAsync(
        GetOverviewQuery request,
        CancellationToken cancellationToken
    )
    {
        var found = await establishments.FindOrNotFoundAsync(
            request.EstablishmentId,
            cancellationToken
        );
        if (!found.IsSuccess)
        {
            return found.Error;
        }
        var establishment = found.Value;
        // The action lets in a caller holding either permission; each section checks its own.
        var permissions = await users.GetPermissionsAsync(currentUser.UserId, cancellationToken);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var documentsOverview = permissions.Contains(Permissions.Documents.Read)
            ? await DocumentsAsync(establishment.Id, today, cancellationToken)
            : null;
        if (!permissions.Contains(Permissions.Inspections.Read))
        {
            return new OverviewResponse(documentsOverview);
        }

        // Only Completed ones: a newer failed or running inspection does not hide the latest result.
        var completed = await inspections.ListCompletedAsync(
            establishment.Id,
            SeriesLength,
            cancellationToken
        );
        var series = completed
            .Reverse()
            .Select(row => new CompliancePoint(
                row.Id,
                row.CompletedAt,
                Compliance(row.Compliant, row.Evaluated)
            ))
            .ToArray();
        if (completed is not [var latest, ..])
        {
            return new InspectionsOverviewResponse(null, 0, 0, 0, series, documentsOverview);
        }

        var findings = await inspections.ListFindingTalliesAsync(latest.Id, cancellationToken);
        var open = findings
            .Where(finding => finding.ActionStatus == ActionItemStatus.Open)
            .ToArray();
        return new InspectionsOverviewResponse(
            new LatestInspectionOverview(
                latest.Id,
                latest.CompletedAt,
                InspectionResponse.RegulationOf(establishment.Jurisdiction),
                Compliance(latest.Compliant, latest.Evaluated),
                VerdictCounts.Of(findings.Select(finding => finding.Verdict)),
                findings
                    .GroupBy(finding => finding.Theme)
                    .OrderBy(theme => Array.IndexOf(ThemeOrder, theme.Key))
                    .Select(ThemeVerdictsOf)
                    .ToArray(),
                findings
                    .GroupBy(finding => finding.Theme)
                    .Select(theme => new ThemeRisk(theme.Key, theme.Sum(Risk)))
                    .Where(theme => theme.Score > 0)
                    .OrderByDescending(theme => theme.Score)
                    .ThenBy(theme => Array.IndexOf(ThemeOrder, theme.Theme))
                    .Take(3)
                    .ToArray()
            ),
            open.Length,
            open.Count(finding => finding.ActionDueOn < today),
            findings.Count(finding => finding.ReviewStatus == ReviewStatus.Pending),
            series,
            documentsOverview
        );
    }

    // Filtered in memory: an establishment holds at most MaxDocumentsPerEstablishment.
    private async Task<DocumentsOverview> DocumentsAsync(
        Guid establishmentId,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var dated = (await documents.ListAsync(establishmentId, cancellationToken))
            .Where(document => document.ExpiresOn <= today.AddDays(ExpiringWithinDays))
            .Select(document => new ExpiringDocument(
                document.Id,
                document.Title,
                document.Type,
                document.ExpiresOn!.Value
            ))
            .OrderBy(document => document.ExpiresOn)
            .ThenBy(document => document.Id)
            .ToArray();
        var expired = dated.Count(document => document.ExpiresOn < today);
        return new DocumentsOverview(
            expired,
            dated.Length - expired,
            dated.Take(DocumentListLength).ToArray()
        );
    }

    private static double? Compliance(int compliant, int evaluated) =>
        evaluated == 0 ? null : (double)compliant / evaluated;

    private static ThemeVerdicts ThemeVerdictsOf(IGrouping<string, FindingTally> theme)
    {
        var counts = VerdictCounts.Of(theme.Select(finding => finding.Verdict));
        return new ThemeVerdicts(
            theme.Key,
            counts.Compliant,
            counts.NonCompliant,
            counts.InsufficientEvidence
        );
    }

    private static double Risk(FindingTally finding) =>
        Severities.RiskWeight(finding.Severity) * Verdicts.RiskFactor(finding.Verdict);
}
