using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Inspections.ListInspections;

/// <summary>The establishment's inspections, newest first, with their findings counted by verdict.</summary>
public sealed record ListInspectionsQuery(Guid EstablishmentId)
    : IQuery<IReadOnlyList<InspectionSummary>>;
