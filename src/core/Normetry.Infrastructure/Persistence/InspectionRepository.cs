using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Normetry.Application.Abstractions;
using Normetry.Application.Inspections;
using Normetry.Domain.Inspections;

namespace Normetry.Infrastructure.Persistence;

// No tenant filter here: row-level security returns and accepts only the transaction's tenant.
// The scope's tenant keys the start lock.
internal sealed class InspectionRepository(CoreDbContext db, ITenantContext tenant)
    : IInspectionRepository
{
    public void Add(Inspection inspection) => db.Inspections.Add(inspection);

    public async Task<Inspection?> GetForUpdateAsync(
        Guid establishmentId,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await LockOfEstablishmentAsync(establishmentId, id, cancellationToken);
        return await db.Inspections.FirstOrDefaultAsync(
            OfEstablishment(establishmentId, id),
            cancellationToken
        );
    }

    public async Task<Inspection?> GetForReviewAsync(
        Guid establishmentId,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        await LockOfEstablishmentAsync(establishmentId, id, cancellationToken);
        return await db
            .Inspections.Include(inspection => inspection.Findings)
                .ThenInclude(finding => finding.ActionItem)
            .AsSplitQuery()
            .FirstOrDefaultAsync(OfEstablishment(establishmentId, id), cancellationToken);
    }

    public Task<Inspection?> GetAsync(
        Guid establishmentId,
        Guid id,
        CancellationToken cancellationToken
    ) =>
        db
            .Inspections.AsNoTracking()
            .Include(inspection => inspection.Findings)
                .ThenInclude(finding => finding.ActionItem)
            .AsSplitQuery()
            .FirstOrDefaultAsync(OfEstablishment(establishmentId, id), cancellationToken);

    /// <summary>The inspection, when it is the establishment's: a condition next to RLS, not instead of it.</summary>
    private static Expression<Func<Inspection, bool>> OfEstablishment(
        Guid establishmentId,
        Guid id
    ) => inspection => inspection.Id == id && inspection.EstablishmentId == establishmentId;

    /// <summary>Locks the inspection's row until the transaction ends, when it is the establishment's.</summary>
    private Task LockOfEstablishmentAsync(
        Guid establishmentId,
        Guid id,
        CancellationToken cancellationToken
    ) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT FROM core.inspections WHERE id = {id} AND establishment_id = {establishmentId} FOR UPDATE",
            cancellationToken
        );

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
                // Counted in SQL: the list is not paged, so it never loads the findings.
                new VerdictCounts(
                    inspection.Findings.Count(finding => finding.Verdict == Verdicts.Compliant),
                    inspection.Findings.Count(finding => finding.Verdict == Verdicts.NonCompliant),
                    inspection.Findings.Count(finding =>
                        finding.Verdict == Verdicts.InsufficientEvidence
                    )
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
                    finding.ReviewStatus != ReviewStatus.Rejected
                    && finding.Verdict == Verdicts.Compliant
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

    public Task LockStartsAsync(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({StartsLockClass}, hashtext({tenant.TenantId.ToString()}))",
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
}
