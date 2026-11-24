using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections;

/// <summary>
/// Inspections of the current tenant, with their findings and action items, and its daily start
/// count. Row-level security scopes every call to the tenant of the request's transaction.
/// Changes are saved when the transaction commits.
/// </summary>
public interface IInspectionRepository
{
    void Add(Inspection inspection);

    /// <summary>
    /// The establishment's inspection, tracked and locked until the transaction ends, so the status
    /// events of one inspection run one at a time. Its findings are not loaded: the events only add
    /// them. Another establishment's is neither locked nor returned.
    /// </summary>
    Task<Inspection?> GetForUpdateAsync(
        Guid establishmentId,
        Guid id,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// The establishment's inspection, tracked with its findings and their action items, and locked
    /// until the transaction ends, so the reviews of one inspection run one at a time. Another
    /// establishment's is neither locked nor returned.
    /// </summary>
    Task<Inspection?> GetForReviewAsync(
        Guid establishmentId,
        Guid id,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// The establishment's inspection, with its findings and their action items; another
    /// establishment's is not returned.
    /// </summary>
    Task<Inspection?> GetAsync(Guid establishmentId, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The establishment's inspections, newest first, with their findings counted by verdict.
    /// <see cref="InspectionSummary.Regulation"/> is left empty.
    /// </summary>
    Task<IReadOnlyList<InspectionSummary>> ListAsync(
        Guid establishmentId,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// The establishment's latest <paramref name="count"/> Completed inspections, most recently
    /// completed first, with their findings that are not Rejected counted.
    /// </summary>
    Task<IReadOnlyList<CompletedInspection>> ListCompletedAsync(
        Guid establishmentId,
        int count,
        CancellationToken cancellationToken
    );

    /// <summary>The inspection's findings that are not Rejected, without their texts and citations.</summary>
    Task<IReadOnlyList<FindingTally>> ListFindingTalliesAsync(
        Guid inspectionId,
        CancellationToken cancellationToken
    );

    /// <summary>An inspection of the establishment is Requested or Running.</summary>
    Task<bool> HasInFlightAsync(Guid establishmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Holds the tenant's inspection starts until the transaction ends (an advisory lock on the
    /// tenant id), so concurrent starts of one tenant reach the in-flight count one at a time, on
    /// any day.
    /// </summary>
    Task LockStartsAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// The tenant's inspections that are Running, or Requested since
    /// <paramref name="requestedSince"/>, across its establishments. An older Requested one lost
    /// its message, and the sweeper fails it.
    /// </summary>
    Task<int> CountInFlightAsync(
        DateTimeOffset requestedSince,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Inspections Running since before <paramref name="before"/>, or still Requested since
    /// before it, tracked and locked; the ones another transaction holds are skipped.
    /// </summary>
    Task<IReadOnlyList<Inspection>> ListInFlightSinceForUpdateAsync(
        DateTimeOffset before,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Counts one start on <paramref name="day"/> unless the count already reached
    /// <paramref name="limit"/> (<c>null</c>: no limit). Atomic, so concurrent starts never pass
    /// the limit; part of the transaction, so a start refused later uncounts itself. Returns the
    /// new count, or <c>null</c> when nothing was counted.
    /// </summary>
    Task<int?> TryCountStartAsync(
        Guid tenantId,
        DateOnly day,
        int? limit,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// A Completed inspection: of its findings that are not Rejected, how many there are
/// (<paramref name="Evaluated"/>) and how many are compliant, by their current verdict.
/// </summary>
public sealed record CompletedInspection(
    Guid Id,
    DateTimeOffset CompletedAt,
    int Compliant,
    int Evaluated
);

/// <summary>A finding as the overview counts it: codes, review status and its action, if any.</summary>
public sealed record FindingTally(
    string Theme,
    string Severity,
    string Verdict,
    ReviewStatus ReviewStatus,
    ActionItemStatus? ActionStatus,
    DateOnly? ActionDueOn
);
