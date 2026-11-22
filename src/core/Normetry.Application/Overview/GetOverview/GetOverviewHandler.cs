using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Establishments;
using Normetry.Application.Identity;
using Normetry.Application.Inspections;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.BuildingBlocks.Time;
using Normetry.Domain.Establishments;
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
    private const int TopRiskThemeCount = 3;
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
        var today = clock.UtcToday();
        var documentsOverview = await BuildDocumentsSectionIfAllowedAsync(
            permissions,
            establishment.Id,
            today,
            cancellationToken
        );
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
        var series = BuildComplianceSeries(completed);
        if (completed is not [var latest, ..])
        {
            return new InspectionsOverviewResponse(null, 0, 0, 0, series, documentsOverview);
        }

        var findings = await inspections.ListFindingTalliesAsync(latest.Id, cancellationToken);
        var (openActions, overdueActions) = CountOpenAndOverdueActions(findings, today);
        return new InspectionsOverviewResponse(
            BuildLatestInspectionSection(latest, establishment, findings),
            openActions,
            overdueActions,
            CountPendingReviews(findings),
            series,
            documentsOverview
        );
    }

    /// <summary>
    /// The expired and soon expiring documents, or null when the caller lacks
    /// <c>documents.read</c>.
    /// </summary>
    private async Task<DocumentsOverview?> BuildDocumentsSectionIfAllowedAsync(
        IReadOnlyList<string> permissions,
        Guid establishmentId,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        if (!permissions.Contains(Permissions.Documents.Read))
        {
            return null;
        }
        // Filtered in memory: an establishment holds at most MaxDocumentsPerEstablishment.
        var dated = (await documents.ListAsync(establishmentId, cancellationToken))
            .Where(document =>
                document.IsExpiredOn(today) || document.ExpiresWithin(today, ExpiringWithinDays)
            )
            .OrderBy(document => document.ExpiresOn)
            .ThenBy(document => document.Id)
            .ToArray();
        var expired = dated.Count(document => document.IsExpiredOn(today));
        return new DocumentsOverview(
            expired,
            dated.Length - expired,
            dated
                .Take(DocumentListLength)
                .Select(document => new ExpiringDocument(
                    document.Id,
                    document.Title,
                    document.Type,
                    document.ExpiresOn!.Value
                ))
                .ToArray()
        );
    }

    /// <summary>The compliance of each listed inspection, oldest first.</summary>
    private static CompliancePoint[] BuildComplianceSeries(
        IReadOnlyList<CompletedInspection> completed
    ) =>
        completed
            .Reverse()
            .Select(row => new CompliancePoint(
                row.Id,
                row.CompletedAt,
                Compliance(row.Compliant, row.Evaluated)
            ))
            .ToArray();

    private static LatestInspectionOverview BuildLatestInspectionSection(
        CompletedInspection latest,
        Establishment establishment,
        IReadOnlyList<FindingTally> findings
    ) =>
        new(
            latest.Id,
            latest.CompletedAt,
            InspectionResponse.RegulationOf(establishment.Jurisdiction),
            Compliance(latest.Compliant, latest.Evaluated),
            VerdictCounts.Of(findings.Select(finding => finding.Verdict)),
            CountVerdictsByTheme(findings),
            PickTopRiskThemes(findings)
        );

    /// <summary>The verdict counts of each theme with findings, in checklist order.</summary>
    private static ThemeVerdicts[] CountVerdictsByTheme(IReadOnlyList<FindingTally> findings) =>
        findings
            .GroupBy(finding => finding.Theme)
            .OrderBy(theme => Array.IndexOf(ThemeOrder, theme.Key))
            .Select(theme =>
            {
                var counts = VerdictCounts.Of(theme.Select(finding => finding.Verdict));
                return new ThemeVerdicts(
                    theme.Key,
                    counts.Compliant,
                    counts.NonCompliant,
                    counts.InsufficientEvidence
                );
            })
            .ToArray();

    /// <summary>
    /// The themes with the highest risk score above 0, highest first, ties in checklist order.
    /// </summary>
    private static ThemeRisk[] PickTopRiskThemes(IReadOnlyList<FindingTally> findings) =>
        findings
            .GroupBy(finding => finding.Theme)
            .Select(theme => new ThemeRisk(theme.Key, theme.Sum(Risk)))
            .Where(theme => theme.Score > 0)
            .OrderByDescending(theme => theme.Score)
            .ThenBy(theme => Array.IndexOf(ThemeOrder, theme.Theme))
            .Take(TopRiskThemeCount)
            .ToArray();

    /// <summary>The open action items, and how many of them were due before today.</summary>
    private static (int Open, int Overdue) CountOpenAndOverdueActions(
        IReadOnlyList<FindingTally> findings,
        DateOnly today
    )
    {
        var open = findings
            .Where(finding => finding.ActionStatus == ActionItemStatus.Open)
            .ToArray();
        return (open.Length, open.Count(finding => finding.ActionDueOn < today));
    }

    private static int CountPendingReviews(IReadOnlyList<FindingTally> findings) =>
        findings.Count(finding => finding.ReviewStatus == ReviewStatus.Pending);

    private static double? Compliance(int compliant, int evaluated) =>
        evaluated == 0 ? null : (double)compliant / evaluated;

    private static double Risk(FindingTally finding) =>
        Severities.RiskWeight(finding.Severity) * Verdicts.RiskFactor(finding.Verdict);
}
