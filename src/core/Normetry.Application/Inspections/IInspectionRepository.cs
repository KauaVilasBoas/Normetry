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
    /// Tracked and locked until the transaction ends, so the status events of one inspection run
    /// one at a time. Its findings are not loaded: the events only add them.
    /// </summary>
    Task<Inspection?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Tracked with its findings and their action items, and locked until the transaction ends, so
    /// the reviews of one inspection run one at a time.
    /// </summary>
    Task<Inspection?> GetForReviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>With its findings and their action items.</summary>
    Task<Inspection?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The establishment's inspections, newest first, with their findings counted by verdict.
    /// <see cref="InspectionSummary.Regulation"/> is left empty.
    /// </summary>
    Task<IReadOnlyList<InspectionSummary>> ListAsync(
        Guid establishmentId,
        CancellationToken cancellationToken
    );

    /// <summary>An inspection of the establishment is Requested or Running.</summary>
    Task<bool> HasInFlightAsync(Guid establishmentId, CancellationToken cancellationToken);

    /// <summary>The tenant's inspections that are Requested or Running, across its establishments.</summary>
    Task<int> CountInFlightAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Inspections Running since before <paramref name="before"/>, tracked and locked; the ones
    /// another transaction holds are skipped.
    /// </summary>
    Task<IReadOnlyList<Inspection>> ListRunningSinceForUpdateAsync(
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
/// Tells the web clients watching an establishment that an inspection changed. Called only after
/// the change is committed; a client that misses one refetches on reconnect.
/// </summary>
public interface IInspectionNotifier
{
    Task NotifyAsync(Guid tenantId, InspectionChange change, CancellationToken cancellationToken);
}

/// <summary>An inspection's new status or progress, pushed as <c>InspectionChanged</c>.</summary>
public sealed record InspectionChange(
    Guid InspectionId,
    Guid EstablishmentId,
    InspectionStatus Status,
    int ItemsEvaluated,
    int ItemsTotal,
    IReadOnlyList<ThemeProgress> Themes,
    string? FailureReasonCode
)
{
    internal static InspectionChange From(Inspection inspection) =>
        new(
            inspection.Id,
            inspection.EstablishmentId,
            inspection.Status,
            inspection.ItemsEvaluated,
            inspection.ItemsTotal,
            inspection.Themes,
            inspection.FailureReasonCode
        );
}
