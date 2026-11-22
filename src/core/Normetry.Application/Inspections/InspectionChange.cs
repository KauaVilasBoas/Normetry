using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections;

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
