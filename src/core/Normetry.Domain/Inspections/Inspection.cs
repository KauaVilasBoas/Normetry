using Normetry.Domain.Identity;

namespace Normetry.Domain.Inspections;

/// <summary>
/// A simulated inspection of one establishment, run by the AI service. Its status runs Requested,
/// Running, then Completed or Failed, driven by the AI service's events; every transition returns
/// whether it changed anything, so a late or repeated event is a no-op. A completion is accepted
/// from any other status, after a failure too: a run the core timed out can still finish, and the
/// findings must not be lost. Only one inspection of an establishment is Requested or Running at a
/// time (a partial unique index).
/// </summary>
public sealed class Inspection
{
    public const int FailureCodeMaxLength = 50;
    public const int FailureReasonMaxLength = 500;

    /// <summary>The core's own failure code: Running for longer than the timeout.</summary>
    public const string TimedOut = "timed_out";

    private readonly List<Finding> findings = [];
    private List<ThemeProgress> themes = [];

    private Inspection(
        Guid id,
        Guid tenantId,
        Guid establishmentId,
        string requestedBy,
        DateTimeOffset requestedAt
    )
    {
        Id = id;
        TenantId = tenantId;
        EstablishmentId = establishmentId;
        RequestedBy = requestedBy;
        RequestedAt = requestedAt;
        Status = InspectionStatus.Requested;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid EstablishmentId { get; private set; }

    public InspectionStatus Status { get; private set; }

    /// <summary>The requester's <c>sub</c>, as <c>InspectionRequested</c> carries it.</summary>
    public string RequestedBy { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>When the first progress arrived; null while Requested.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When it ended: completed, or failed (a late completion moves it).</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    public int ItemsTotal { get; private set; }

    public int ItemsEvaluated { get; private set; }

    /// <summary>The per-theme progress of the event with the most items evaluated.</summary>
    public IReadOnlyList<ThemeProgress> Themes => themes;

    public int? InputTokens { get; private set; }

    public int? OutputTokens { get; private set; }

    public decimal? CostUsd { get; private set; }

    /// <summary>A code of the contract's failure reasons, or <see cref="TimedOut"/>.</summary>
    public string? FailureReasonCode { get; private set; }

    public string? FailureReason { get; private set; }

    public IReadOnlyCollection<Finding> Findings => findings;

    /// <summary>A new inspection, Requested by the user <paramref name="requestedBy"/> (a <c>sub</c>).</summary>
    public static Inspection Request(
        Guid tenantId,
        Guid establishmentId,
        string requestedBy,
        DateTimeOffset now
    ) =>
        new(
            Guid.CreateVersion7(),
            Names.RequiredId(tenantId, nameof(tenantId)),
            Names.RequiredId(establishmentId, nameof(establishmentId)),
            Names.Required(requestedBy, User.SubMaxLength, nameof(requestedBy)),
            now
        );

    /// <summary><c>InspectionStarted</c>: Requested becomes Running, with the first snapshot.</summary>
    public bool Start(int itemsTotal, IReadOnlyList<ThemeProgress> snapshot, DateTimeOffset now) =>
        Progress(snapshot.Sum(theme => theme.ItemsEvaluated), itemsTotal, snapshot, now);

    /// <summary>
    /// <c>InspectionProgressed</c> (Running, from Requested too when the start is late). The
    /// snapshot is replaced only by one with more items evaluated: progress never goes back,
    /// since a retried event can arrive after a later one.
    /// </summary>
    public bool Progress(
        int itemsEvaluated,
        int itemsTotal,
        IReadOnlyList<ThemeProgress> snapshot,
        DateTimeOffset now
    )
    {
        if (Status is not (InspectionStatus.Requested or InspectionStatus.Running))
        {
            return false;
        }
        var changed = false;
        if (Status == InspectionStatus.Requested)
        {
            Status = InspectionStatus.Running;
            StartedAt = now;
            changed = true;
        }
        if (themes.Count == 0 || itemsEvaluated > ItemsEvaluated)
        {
            ItemsEvaluated = itemsEvaluated;
            ItemsTotal = itemsTotal;
            themes = [.. snapshot];
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// <c>InspectionCompleted</c>, from any other status (after a failure too). Each finding's
    /// action is due its number of days after the UTC day of completion.
    /// </summary>
    public bool Complete(
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        IReadOnlyList<FindingResult> results,
        DateTimeOffset now
    )
    {
        if (Status == InspectionStatus.Completed)
        {
            return false;
        }
        var completedOn = DateOnly.FromDateTime(now.UtcDateTime);
        // All or nothing: a finding off the rules throws before anything changes.
        findings.AddRange([.. results.Select(result => Finding.Create(this, result, completedOn))]);
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
        FailureReasonCode = null;
        FailureReason = null;
        Status = InspectionStatus.Completed;
        CompletedAt = now;
        return true;
    }

    /// <summary><c>InspectionFailed</c>: Failed, from Requested or Running only.</summary>
    public bool Fail(string code, string reason, DateTimeOffset now)
    {
        if (Status is not (InspectionStatus.Requested or InspectionStatus.Running))
        {
            return false;
        }
        FailureReasonCode = Truncate(code, FailureCodeMaxLength);
        FailureReason = Truncate(reason, FailureReasonMaxLength);
        Status = InspectionStatus.Failed;
        CompletedAt = now;
        return true;
    }

    /// <summary>
    /// Running for at least <paramref name="timeout"/>: Failed with <see cref="TimedOut"/>.
    /// Requested never times out: the queue is durable and the inspection waits for the AI service.
    /// </summary>
    public bool TimeOut(TimeSpan timeout, DateTimeOffset now) =>
        Status == InspectionStatus.Running
        && now - StartedAt!.Value >= timeout
        && Fail(TimedOut, "The inspection took too long and was stopped.", now);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

public enum InspectionStatus
{
    /// <summary>Accepted and announced; waits for the AI service.</summary>
    Requested = 1,
    Running,
    Completed,
    Failed,
}

/// <summary>One theme of a progress snapshot (also the payload of the progress messages).</summary>
public sealed record ThemeProgress(string Theme, int ItemsTotal, int ItemsEvaluated)
{
    /// <summary>The checklist themes, in checklist order.</summary>
    public static readonly IReadOnlySet<string> Themes = new HashSet<string>(StringComparer.Ordinal)
    {
        "personal_hygiene",
        "temperature_control",
        "cross_contamination_allergens",
        "cleaning_sanitizing",
        "pest_control",
        "water_premises",
        "waste",
        "documentation_training",
    };
}
