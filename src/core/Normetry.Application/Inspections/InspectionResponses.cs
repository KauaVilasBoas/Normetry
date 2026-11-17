using Normetry.Application.Identity;
using Normetry.Domain.Establishments;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections;

/// <summary>
/// A row of the establishment's inspections. <see cref="FailureReasonCode"/> is a contract reason
/// code or <c>timed_out</c>; the web app shows its own text for it.
/// </summary>
public sealed record InspectionSummary(
    Guid Id,
    InspectionStatus Status,
    string Regulation,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int ItemsTotal,
    int ItemsEvaluated,
    VerdictCounts VerdictCounts,
    string? FailureReasonCode
);

/// <summary>The inspection's findings by their verdict as it stands (a reviewer's edit included).</summary>
public sealed record VerdictCounts(int Compliant, int NonCompliant, int InsufficientEvidence);

/// <summary>An inspection with its progress and its findings, most severe first.</summary>
public sealed record InspectionResponse(
    Guid Id,
    Guid EstablishmentId,
    InspectionStatus Status,
    string Regulation,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int ItemsTotal,
    int ItemsEvaluated,
    IReadOnlyList<ThemeProgress> Themes,
    string? FailureReasonCode,
    IReadOnlyList<FindingResponse> Findings
)
{
    private static readonly string[] SeverityOrder = ["critical", "major", "minor"];

    internal static InspectionResponse From(
        Inspection inspection,
        string regulation,
        IReadOnlyDictionary<Guid, string?> users
    ) =>
        new(
            inspection.Id,
            inspection.EstablishmentId,
            inspection.Status,
            regulation,
            inspection.RequestedAt,
            inspection.StartedAt,
            inspection.CompletedAt,
            inspection.ItemsTotal,
            inspection.ItemsEvaluated,
            inspection.Themes,
            inspection.FailureReasonCode,
            inspection
                .Findings.OrderBy(finding => Array.IndexOf(SeverityOrder, finding.Severity))
                .ThenBy(finding => finding.ItemId, StringComparer.Ordinal)
                .Select(finding => FindingResponse.From(finding, users))
                .ToArray()
        );

    /// <summary>The reviewers and action updaters, whose names the response shows.</summary>
    internal static IEnumerable<Guid> UserIds(Inspection inspection) =>
        inspection
            .Findings.SelectMany(finding =>
                new[] { finding.ReviewedBy, finding.ActionItem?.UpdatedBy }
            )
            .OfType<Guid>()
            .Distinct();

    /// <summary>
    /// The regulation an establishment of <paramref name="jurisdiction"/> is inspected against,
    /// named as the web app names it.
    /// </summary>
    internal static string RegulationOf(Jurisdiction jurisdiction) =>
        jurisdiction switch
        {
            Jurisdiction.US => "FDA Food Code 2026",
            Jurisdiction.EU => "Regulation (EC) 852/2004",
            Jurisdiction.BR => "Anvisa RDC 216/2004",
            _ => throw new ArgumentOutOfRangeException(nameof(jurisdiction), jurisdiction, null),
        };
}

/// <summary>
/// A finding with copies of its passages and its review. <see cref="Original"/> holds the
/// inspection's values once a reviewer edited it; <see cref="ReviewNote"/> says why it was rejected.
/// </summary>
public sealed record FindingResponse(
    Guid Id,
    string ItemId,
    string Theme,
    string Title,
    string Severity,
    string Verdict,
    string Confidence,
    string Rationale,
    IReadOnlyList<RegulationCitation> RegulationCitations,
    IReadOnlyList<EvidenceCitation> EvidenceCitations,
    ReviewStatus ReviewStatus,
    UserReference? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote,
    FindingOriginal? Original,
    ActionItemResponse? ActionItem
)
{
    internal static FindingResponse From(
        Finding finding,
        IReadOnlyDictionary<Guid, string?> users
    ) =>
        new(
            finding.Id,
            finding.ItemId,
            finding.Theme,
            finding.Title,
            finding.Severity,
            finding.Verdict,
            finding.Confidence,
            finding.Rationale,
            finding.RegulationCitations,
            finding.EvidenceCitations,
            finding.ReviewStatus,
            User(finding.ReviewedBy, users),
            finding.ReviewedAt,
            finding.ReviewNote,
            finding.Original,
            finding.ActionItem is { } item
                ? new ActionItemResponse(
                    item.Id,
                    item.Text,
                    item.DueOn,
                    item.Status,
                    User(item.UpdatedBy, users),
                    item.UpdatedAt
                )
                : null
        );

    private static UserReference? User(Guid? id, IReadOnlyDictionary<Guid, string?> users) =>
        id is { } user ? new UserReference(user, users.GetValueOrDefault(user)) : null;
}

/// <summary>A finding's action. <see cref="UpdatedBy"/> is null until a person changes it.</summary>
public sealed record ActionItemResponse(
    Guid Id,
    string Text,
    DateOnly DueOn,
    ActionItemStatus Status,
    UserReference? UpdatedBy,
    DateTimeOffset? UpdatedAt
);

/// <summary>The id of the inspection a start created.</summary>
public sealed record StartedInspectionResponse(Guid Id);
