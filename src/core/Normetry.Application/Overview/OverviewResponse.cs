using System.Text.Json.Serialization;
using Normetry.Application.Inspections;
using Normetry.Domain.Documents;

namespace Normetry.Application.Overview;

/// <summary>
/// An establishment's overview for a caller without <c>inspections.read</c>: the inspection
/// sections are not part of this type, so they are left out of the JSON. Without
/// <c>documents.read</c>, <see cref="Documents"/> is null and left out too.
/// </summary>
public record OverviewResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        DocumentsOverview? Documents
);

/// <summary>
/// The overview with its inspection sections, for a caller with <c>inspections.read</c>.
/// <see cref="LatestInspection"/> is the latest Completed inspection (null before the first one);
/// the action and review counts are its own. <see cref="Series"/> holds the latest Completed
/// inspections, oldest first.
/// </summary>
public sealed record InspectionsOverviewResponse(
    LatestInspectionOverview? LatestInspection,
    int OpenActionItems,
    int OverdueActionItems,
    int PendingReviews,
    IReadOnlyList<CompliancePoint> Series,
    DocumentsOverview? Documents
) : OverviewResponse(Documents);

/// <summary>
/// The latest Completed inspection's results, by each finding's current verdict, Rejected findings
/// left out. <see cref="Compliance"/> is compliant ÷ the rest (0 to 1), null with no finding left.
/// <see cref="TopRiskThemes"/> are the (at most 3) themes with the highest severity-weighted
/// non-compliance.
/// </summary>
public sealed record LatestInspectionOverview(
    Guid Id,
    DateTimeOffset CompletedAt,
    string Regulation,
    double? Compliance,
    VerdictCounts VerdictCounts,
    IReadOnlyList<ThemeVerdicts> ByTheme,
    IReadOnlyList<ThemeRisk> TopRiskThemes
);

public sealed record ThemeVerdicts(
    string Theme,
    int Compliant,
    int NonCompliant,
    int InsufficientEvidence
);

/// <summary>
/// Each non-compliant finding weighs its severity (critical 3, major 2, minor 1), an
/// insufficient-evidence one half of it.
/// </summary>
public sealed record ThemeRisk(string Theme, double Score);

public sealed record CompliancePoint(
    Guid InspectionId,
    DateTimeOffset CompletedAt,
    double? Compliance
);

/// <summary>
/// The documents (not deleted) whose expiry date passed, or comes within 30 days, and the
/// soonest of them.
/// </summary>
public sealed record DocumentsOverview(
    int Expired,
    int ExpiringSoon,
    IReadOnlyList<ExpiringDocument> List
);

public sealed record ExpiringDocument(Guid Id, string Title, DocumentType Type, DateOnly ExpiresOn);
