using Microsoft.EntityFrameworkCore;
using Normetry.Application.Inspections;
using Normetry.Domain.Inspections;

namespace Normetry.Infrastructure.Persistence;

// No tenant filter here: row-level security returns and accepts only the transaction's tenant.
internal sealed class InspectionRepository(CoreDbContext db) : IInspectionRepository
{
    public void Add(Inspection inspection) => db.Inspections.Add(inspection);

    public async Task<Inspection?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT FROM core.inspections WHERE id = {id} FOR UPDATE",
            cancellationToken
        );
        return await db.Inspections.FirstOrDefaultAsync(
            inspection => inspection.Id == id,
            cancellationToken
        );
    }

    public async Task<Inspection?> GetForReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT FROM core.inspections WHERE id = {id} FOR UPDATE",
            cancellationToken
        );
        return await db
            .Inspections.Include(inspection => inspection.Findings)
                .ThenInclude(finding => finding.ActionItem)
            .AsSplitQuery()
            .FirstOrDefaultAsync(inspection => inspection.Id == id, cancellationToken);
    }

    public Task<Inspection?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db
            .Inspections.AsNoTracking()
            .Include(inspection => inspection.Findings)
                .ThenInclude(finding => finding.ActionItem)
            .AsSplitQuery()
            .FirstOrDefaultAsync(inspection => inspection.Id == id, cancellationToken);

    public async Task<IReadOnlyList<InspectionSummary>> ListAsync(
        Guid establishmentId,
        CancellationToken cancellationToken
    ) =>
        await db
            .Inspections.AsNoTracking()
            .Where(inspection => inspection.EstablishmentId == establishmentId)
            .OrderByDescending(inspection => inspection.RequestedAt)
            .Select(inspection => new InspectionSummary(
                inspection.Id,
                inspection.Status,
                "",
                inspection.RequestedAt,
                inspection.StartedAt,
                inspection.CompletedAt,
                inspection.ItemsTotal,
                inspection.ItemsEvaluated,
                new VerdictCounts(
                    inspection.Findings.Count(finding => finding.Verdict == "compliant"),
                    inspection.Findings.Count(finding => finding.Verdict == "non_compliant"),
                    inspection.Findings.Count(finding => finding.Verdict == "insufficient_evidence")
                ),
                inspection.FailureReasonCode
            ))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CompletedInspection>> ListCompletedAsync(
        Guid establishmentId,
        int count,
        CancellationToken cancellationToken
    ) =>
        await db
            .Inspections.AsNoTracking()
            .Where(inspection =>
                inspection.EstablishmentId == establishmentId
                && inspection.Status == InspectionStatus.Completed
            )
            .OrderByDescending(inspection => inspection.CompletedAt)
            .ThenByDescending(inspection => inspection.Id)
            .Take(count)
            .Select(inspection => new CompletedInspection(
                inspection.Id,
                inspection.CompletedAt!.Value,
                inspection.Findings.Count(finding =>
                    finding.ReviewStatus != ReviewStatus.Rejected && finding.Verdict == "compliant"
                ),
                inspection.Findings.Count(finding => finding.ReviewStatus != ReviewStatus.Rejected)
            ))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FindingTally>> ListFindingTalliesAsync(
        Guid inspectionId,
        CancellationToken cancellationToken
    ) =>
        await db
            .Findings.AsNoTracking()
            .Where(finding =>
                finding.InspectionId == inspectionId
                && finding.ReviewStatus != ReviewStatus.Rejected
            )
            .Select(finding => new FindingTally(
                finding.Theme,
                finding.Severity,
                finding.Verdict,
                finding.ReviewStatus,
                finding.ActionItem == null ? null : finding.ActionItem.Status,
                finding.ActionItem == null ? null : finding.ActionItem.DueOn
            ))
            .ToListAsync(cancellationToken);

    public Task<bool> HasInFlightAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        db.Inspections.AnyAsync(
            inspection =>
                inspection.EstablishmentId == establishmentId
                && (
                    inspection.Status == InspectionStatus.Requested
                    || inspection.Status == InspectionStatus.Running
                ),
            cancellationToken
        );

    // The first key of the two-key advisory locks taken on inspection starts, so they never meet
    // another advisory lock; a hash collision between tenants only serializes their starts.
    private const int StartsLockClass = 1;

    public Task LockStartsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({StartsLockClass}, hashtext({tenantId.ToString()}))",
            cancellationToken
        );

    // The partial unique index on (tenant_id, establishment_id) for these statuses serves it.
    public Task<int> CountInFlightAsync(
        DateTimeOffset requestedSince,
        CancellationToken cancellationToken
    ) =>
        db.Inspections.CountAsync(
            inspection =>
                inspection.Status == InspectionStatus.Running
                || inspection.Status == InspectionStatus.Requested
                    && inspection.RequestedAt >= requestedSince,
            cancellationToken
        );

    public async Task<IReadOnlyList<Inspection>> ListInFlightSinceForUpdateAsync(
        DateTimeOffset before,
        CancellationToken cancellationToken
    )
    {
        var ids = await db
            .Database.SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM core.inspections
                WHERE status = 'Running' AND started_at < {before}
                    OR status = 'Requested' AND requested_at < {before}
                FOR UPDATE SKIP LOCKED
                """
            )
            .ToListAsync(cancellationToken);
        return ids.Count == 0
            ? []
            : await db
                .Inspections.Where(inspection => ids.Contains(inspection.Id))
                .ToListAsync(cancellationToken);
    }

    public async Task<int?> TryCountStartAsync(
        Guid tenantId,
        DateOnly day,
        int? limit,
        CancellationToken cancellationToken
    )
    {
        // The chat counter's statement: the first start of the day inserts the row, later ones
        // update it under the row lock ON CONFLICT takes, and the WHERE re-checks the latest
        // count. No row comes back at the limit.
        var counts = await db
            .Database.SqlQuery<int>(
                $"""
                INSERT INTO core.inspection_counts AS c (tenant_id, day, count)
                VALUES ({tenantId}, {day}, 1)
                ON CONFLICT (tenant_id, day) DO UPDATE SET count = c.count + 1
                    WHERE {limit}::integer IS NULL OR c.count < {limit}::integer
                RETURNING c.count AS "Value"
                """
            )
            .ToListAsync(cancellationToken);
        return counts is [var count] ? count : null;
    }
}
