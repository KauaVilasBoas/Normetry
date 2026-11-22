using Normetry.Domain.Documents;

namespace Normetry.Domain.Inspections;

/// <summary>
/// One checklist item's verdict in an <see cref="Inspection"/>, with copies of the passages it
/// cites. It starts Pending; a reviewer accepts, rejects (with a note) or edits it, and the latest
/// decision wins. The first edit keeps the inspection's values in <see cref="Original"/>.
/// </summary>
public sealed class Finding
{
    public const int ItemIdMaxLength = 100;
    public const int TitleMaxLength = 200;

    /// <summary>What the AI service may send; a reviewer's rationale is shorter.</summary>
    public const int RationaleMaxLength = 4000;
    public const int EditedRationaleMaxLength = 2000;
    public const int ReviewNoteMaxLength = 1000;

    /// <summary>Why <see cref="NeedsActionTextAndDueOn"/> refuses an edit.</summary>
    public const string ActionNeedsTextAndDueOnMessage =
        "A finding with no action needs both the action's text and its due date.";

    private List<RegulationCitation> regulationCitations;
    private List<EvidenceCitation> evidenceCitations;

    private Finding(
        Guid id,
        Guid tenantId,
        Guid inspectionId,
        string itemId,
        string theme,
        string title,
        string severity,
        string verdict,
        string confidence,
        string rationale
    )
    {
        Id = id;
        TenantId = tenantId;
        InspectionId = inspectionId;
        ItemId = itemId;
        Theme = theme;
        Title = title;
        Severity = severity;
        Verdict = verdict;
        Confidence = confidence;
        Rationale = rationale;
        ReviewStatus = ReviewStatus.Pending;
        regulationCitations = [];
        evidenceCitations = [];
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid InspectionId { get; private set; }

    /// <summary>The checklist item's id.</summary>
    public string ItemId { get; private set; }

    /// <summary>One of <see cref="ThemeProgress.Themes"/>.</summary>
    public string Theme { get; private set; }

    public string Title { get; private set; }

    /// <summary>One of <see cref="Severities.All"/>.</summary>
    public string Severity { get; private set; }

    /// <summary>One of <see cref="Verdicts.All"/>.</summary>
    public string Verdict { get; private set; }

    /// <summary>One of <see cref="Confidences.All"/>; a reviewer does not change it.</summary>
    public string Confidence { get; private set; }

    /// <summary>English.</summary>
    public string Rationale { get; private set; }

    public IReadOnlyList<RegulationCitation> RegulationCitations => regulationCitations;

    public IReadOnlyList<EvidenceCitation> EvidenceCitations => evidenceCitations;

    public ReviewStatus ReviewStatus { get; private set; }

    /// <summary>The <c>core.users</c> id of the latest reviewer; null while Pending.</summary>
    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>Why it was rejected; null in any other status.</summary>
    public string? ReviewNote { get; private set; }

    /// <summary>The inspection's values, kept by the first edit; null before it.</summary>
    public FindingOriginal? Original { get; private set; }

    /// <summary>What to do about it; none for most compliant findings.</summary>
    public ActionItem? ActionItem { get; private set; }

    internal static Finding Create(
        Inspection inspection,
        FindingResult result,
        DateOnly completedOn
    )
    {
        var finding = new Finding(
            Guid.CreateVersion7(),
            inspection.TenantId,
            inspection.Id,
            Names.Required(result.ItemId, ItemIdMaxLength, nameof(result)),
            Code(result.Theme, ThemeProgress.Themes, nameof(result)),
            Names.Required(result.Title, TitleMaxLength, nameof(result)),
            Code(result.Severity, Severities.All, nameof(result)),
            Code(result.Verdict, Verdicts.All, nameof(result)),
            Code(result.Confidence, Confidences.All, nameof(result)),
            Names.Required(result.Rationale, RationaleMaxLength, nameof(result))
        )
        {
            regulationCitations = [.. result.RegulationCitations],
            evidenceCitations = [.. result.EvidenceCitations],
        };
        if (result.Action is { } action)
        {
            finding.ActionItem = ActionItem.Create(
                finding,
                action.Text,
                completedOn.AddDays(action.DueInDays),
                updatedBy: null,
                updatedAt: null
            );
        }
        return finding;
    }

    public void Accept(Guid reviewer, DateTimeOffset now) =>
        Review(ReviewStatus.Accepted, reviewer, note: null, now);

    /// <summary>Rejects it with a <paramref name="note"/> saying why, which is required.</summary>
    public void Reject(Guid reviewer, string note, DateTimeOffset now) =>
        Review(
            ReviewStatus.Rejected,
            reviewer,
            Names.Required(note, ReviewNoteMaxLength, nameof(note)),
            now
        );

    /// <summary>
    /// Changes the fields <paramref name="edit"/> gives; the others stay. A finding with no action
    /// gets one when both its text and due date are given, and refuses only one of the two. Every
    /// value is checked before anything changes.
    /// </summary>
    public void Edit(Guid reviewer, FindingEdit edit, DateTimeOffset now)
    {
        if (edit.IsEmpty)
        {
            throw new ArgumentException("An edit changes at least one field.", nameof(edit));
        }
        if (NeedsActionTextAndDueOn(edit))
        {
            throw new ArgumentException(ActionNeedsTextAndDueOnMessage, nameof(edit));
        }
        Names.RequiredId(reviewer, nameof(reviewer));
        var verdict = edit.Verdict is null
            ? Verdict
            : Code(edit.Verdict, Verdicts.All, nameof(edit));
        var severity = edit.Severity is null
            ? Severity
            : Code(edit.Severity, Severities.All, nameof(edit));
        var rationale = edit.Rationale is null
            ? Rationale
            : Names.Required(edit.Rationale, EditedRationaleMaxLength, nameof(edit));
        var actionText = edit.ActionText is null
            ? null
            : Names.Required(edit.ActionText, ActionItem.EditedTextMaxLength, nameof(edit));

        Original ??= new FindingOriginal(
            Verdict,
            Severity,
            Rationale,
            ActionItem?.Text,
            ActionItem?.DueOn
        );
        Verdict = verdict;
        Severity = severity;
        Rationale = rationale;
        if (ActionItem is null)
        {
            if (actionText is not null)
            {
                ActionItem = ActionItem.Create(this, actionText, edit.DueOn!.Value, reviewer, now);
            }
        }
        else if (actionText is not null || edit.DueOn is not null)
        {
            ActionItem.Change(actionText, edit.DueOn, reviewer, now);
        }
        Review(ReviewStatus.Edited, reviewer, note: null, now);
    }

    /// <summary>
    /// Whether <paramref name="edit"/> gives only one of the action's text and due date while this
    /// finding has no action: creating one needs both.
    /// </summary>
    public bool NeedsActionTextAndDueOn(FindingEdit edit) =>
        ActionItem is null && (edit.ActionText is null) != (edit.DueOn is null);

    private void Review(ReviewStatus status, Guid reviewer, string? note, DateTimeOffset now)
    {
        ReviewedBy = Names.RequiredId(reviewer, nameof(reviewer));
        ReviewStatus = status;
        ReviewedAt = now;
        ReviewNote = note;
    }

    private static string Code(string value, IReadOnlySet<string> codes, string paramName) =>
        codes.Contains(value)
            ? value
            : throw new ArgumentException($"'{value}' is not a known code.", paramName);
}

/// <summary>A finding's verdict codes, as the inspection contract writes them.</summary>
public static class Verdicts
{
    public const string Compliant = "compliant";
    public const string NonCompliant = "non_compliant";
    public const string InsufficientEvidence = "insufficient_evidence";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Compliant,
        NonCompliant,
        InsufficientEvidence,
    };

    /// <summary>
    /// The share of its severity's weight a finding adds to its theme's risk: all of it when
    /// non-compliant, half when the evidence is insufficient, none when compliant.
    /// </summary>
    public static double RiskFactor(string verdict) =>
        verdict switch
        {
            NonCompliant => 1,
            InsufficientEvidence => 0.5,
            _ => 0,
        };
}

/// <summary>A finding's severity codes, as the inspection contract writes them.</summary>
public static class Severities
{
    public const string Critical = "critical";
    public const string Major = "major";
    public const string Minor = "minor";

    /// <summary>The severities, most severe first.</summary>
    public static readonly IReadOnlyList<string> InOrder = [Critical, Major, Minor];

    /// <summary>The same severities as a set; a set's order is not guaranteed.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        InOrder,
        StringComparer.Ordinal
    );

    /// <summary>What a finding of <paramref name="severity"/> weighs in its theme's risk: critical 3, major 2, minor 1.</summary>
    public static int RiskWeight(string severity) =>
        severity switch
        {
            Critical => 3,
            Major => 2,
            _ => 1,
        };
}

/// <summary>A finding's confidence codes, as the inspection contract writes them.</summary>
public static class Confidences
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Low,
        Medium,
        High,
    };
}

public enum ReviewStatus
{
    Pending = 1,
    Accepted,
    Rejected,
    Edited,
}

/// <summary>
/// One checklist item's verdict as the AI service sends it in <c>InspectionCompleted</c>.
/// <see cref="Action"/> is null for <see cref="Verdicts.Compliant"/> only.
/// </summary>
public sealed record FindingResult(
    string ItemId,
    string Theme,
    string Title,
    string Severity,
    string Verdict,
    string Confidence,
    string Rationale,
    IReadOnlyList<RegulationCitation> RegulationCitations,
    IReadOnlyList<EvidenceCitation> EvidenceCitations,
    ProposedAction? Action
);

/// <summary>What to do, due <see cref="DueInDays"/> days after completion.</summary>
public sealed record ProposedAction(string Text, int DueInDays);

/// <summary>A reviewer's changes: each null field stays as it is.</summary>
public sealed record FindingEdit(
    string? Verdict = null,
    string? Severity = null,
    string? Rationale = null,
    string? ActionText = null,
    DateOnly? DueOn = null
)
{
    /// <summary>It gives no field, so it would change nothing.</summary>
    public bool IsEmpty =>
        this is { Verdict: null, Severity: null, Rationale: null, ActionText: null, DueOn: null };
}

/// <summary>A finding's values before its first edit; the action is null when it had none.</summary>
public sealed record FindingOriginal(
    string Verdict,
    string Severity,
    string Rationale,
    string? ActionText,
    DateOnly? DueOn
);

/// <summary>
/// A copy of a corpus passage. <c>Text[QuoteStart..QuoteEnd]</c> is the quote; both null when it
/// could not be mapped back.
/// </summary>
public sealed record RegulationCitation(
    string SourceTitle,
    string SourceVersion,
    string SectionPath,
    string Language,
    string Text,
    string Quote,
    int? QuoteStart,
    int? QuoteEnd
);

/// <summary>
/// A copy of a passage of one of the establishment's documents. The title and type are null for a
/// document uploaded before <c>DocumentUploaded</c> carried them.
/// </summary>
public sealed record EvidenceCitation(
    Guid DocumentId,
    string? DocumentTitle,
    DocumentType? DocumentType,
    int VersionNumber,
    string SectionPath,
    string Language,
    string Text,
    string Quote,
    int? QuoteStart,
    int? QuoteEnd
);
