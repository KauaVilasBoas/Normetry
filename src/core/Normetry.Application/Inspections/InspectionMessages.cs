using Normetry.Application.Abstractions;
using Normetry.Domain.Establishments;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections;

// The payloads of the inspection messages (src/shared/contracts/messages, version 1). The parts the
// core stores as they come (ThemeProgress, FindingResult and its citations) are domain records.
// Enums of the domain travel by name (Jurisdiction, EstablishmentType, DocumentType); the
// contract's own codes (theme, severity, verdict, confidence, reasonCode) are snake_case strings,
// checked by the reader against the domain's sets (ThemeProgress.Themes, Finding.Severities,
// Finding.Verdicts, Finding.Confidences) and InspectionFailed.ReasonCodes. A new code is a new
// message version.

/// <summary>An ai to core event about one inspection: what the status consumer applies.</summary>
public interface IInspectionEvent
{
    Guid EstablishmentId { get; }

    Guid InspectionId { get; }
}

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

/// <summary>
/// ai to core: the AI service started running an inspection. <see cref="Themes"/> is a full
/// snapshot, never a delta.
/// </summary>
public sealed record InspectionStarted(
    Guid EstablishmentId,
    Guid InspectionId,
    int ItemsTotal,
    IReadOnlyList<ThemeProgress> Themes
) : IIntegrationMessage, IInspectionEvent
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
    IReadOnlyList<ThemeProgress> Themes
) : IIntegrationMessage, IInspectionEvent
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
    IReadOnlyList<FindingResult> Findings
) : IIntegrationMessage, IInspectionEvent
{
    public static string Type => "InspectionCompleted";

    public static int Version => 1;

    public static string RoutingKey => "inspection.completed.v1";
}

/// <summary>ai to core: an inspection stopped without findings.</summary>
public sealed record InspectionFailed(
    Guid EstablishmentId,
    Guid InspectionId,
    string ReasonCode,
    string ReasonText
) : IIntegrationMessage, IInspectionEvent
{
    public const int ReasonTextMaxLength = 500;

    public static string Type => "InspectionFailed";

    public static int Version => 1;

    public static string RoutingKey => "inspection.failed.v1";
}
