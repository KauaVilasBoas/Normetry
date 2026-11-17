using Normetry.Application.Abstractions;
using Normetry.Domain.Documents;
using Normetry.Domain.Establishments;

namespace Normetry.Application.Inspections;

// The payloads of the inspection messages (src/shared/contracts/messages, version 1). Enums of the
// domain travel by name (Jurisdiction, EstablishmentType, DocumentType); the contract's own codes
// (theme, severity, verdict, confidence, reasonCode) are snake_case strings, checked by the reader
// against the sets below. A new code is a new message version.

/// <summary>core to ai: a user started a simulated inspection of an establishment.</summary>
public sealed record InspectionRequested(
    Guid EstablishmentId,
    Guid InspectionId,
    Jurisdiction Jurisdiction,
    EstablishmentType EstablishmentType,
    int TokenCeiling,
    string RequestedBy
) : IIntegrationMessage
{
    public const int RequestedByMaxLength = 255;

    public static string Type => "InspectionRequested";

    public static int Version => 1;

    public static string RoutingKey => "inspection.requested.v1";
}

/// <summary>One theme of a progress snapshot.</summary>
public sealed record InspectionThemeProgress(string Theme, int ItemsTotal, int ItemsEvaluated)
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

/// <summary>
/// ai to core: the AI service started running an inspection. <see cref="Themes"/> is a full
/// snapshot, never a delta.
/// </summary>
public sealed record InspectionStarted(
    Guid EstablishmentId,
    Guid InspectionId,
    int ItemsTotal,
    IReadOnlyList<InspectionThemeProgress> Themes
) : IIntegrationMessage
{
    public static string Type => "InspectionStarted";

    public static int Version => 1;

    public static string RoutingKey => "inspection.started.v1";
}

/// <summary>
/// ai to core: a running inspection evaluated more items. Progress never goes back: keep the
/// event with the highest <see cref="ItemsEvaluated"/>, since a retried one can arrive late.
/// </summary>
public sealed record InspectionProgressed(
    Guid EstablishmentId,
    Guid InspectionId,
    int ItemsEvaluated,
    int ItemsTotal,
    IReadOnlyList<InspectionThemeProgress> Themes
) : IIntegrationMessage
{
    public static string Type => "InspectionProgressed";

    public static int Version => 1;

    public static string RoutingKey => "inspection.progressed.v1";
}

/// <summary>ai to core: an inspection finished with its findings.</summary>
public sealed record InspectionCompleted(
    Guid EstablishmentId,
    Guid InspectionId,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    IReadOnlyList<InspectionFinding> Findings
) : IIntegrationMessage
{
    public static string Type => "InspectionCompleted";

    public static int Version => 1;

    public static string RoutingKey => "inspection.completed.v1";
}

/// <summary>
/// One checklist item's verdict. <see cref="Action"/> is null for <c>compliant</c> only.
/// </summary>
public sealed record InspectionFinding(
    string ItemId,
    string Theme,
    string Title,
    string Severity,
    string Verdict,
    string Confidence,
    string Rationale,
    IReadOnlyList<InspectionRegulationCitation> RegulationCitations,
    IReadOnlyList<InspectionEvidenceCitation> EvidenceCitations,
    InspectionAction? Action
)
{
    public static readonly IReadOnlySet<string> Severities = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "critical",
        "major",
        "minor",
    };

    public static readonly IReadOnlySet<string> Verdicts = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "compliant",
        "non_compliant",
        "insufficient_evidence",
    };

    public static readonly IReadOnlySet<string> Confidences = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "low",
        "medium",
        "high",
    };
}

/// <summary>
/// A copy of a corpus passage. <c>Text[QuoteStart..QuoteEnd]</c> is the quote; both null when it
/// could not be mapped back.
/// </summary>
public sealed record InspectionRegulationCitation(
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
public sealed record InspectionEvidenceCitation(
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

/// <summary>What to do, due <see cref="DueInDays"/> days after completion.</summary>
public sealed record InspectionAction(string Text, int DueInDays);

/// <summary>ai to core: an inspection stopped without findings.</summary>
public sealed record InspectionFailed(
    Guid EstablishmentId,
    Guid InspectionId,
    string ReasonCode,
    string ReasonText
) : IIntegrationMessage
{
    public const int ReasonTextMaxLength = 500;

    public static string Type => "InspectionFailed";

    public static int Version => 1;

    public static string RoutingKey => "inspection.failed.v1";

    /// <summary>The contract's codes; the core's own <c>timed_out</c> is not one.</summary>
    public static readonly IReadOnlySet<string> ReasonCodes = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "budget_exceeded",
        "no_documents",
        "error",
    };
}
