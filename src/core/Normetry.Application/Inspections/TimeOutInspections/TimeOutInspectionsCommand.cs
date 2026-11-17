using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Inspections.TimeOutInspections;

/// <summary>
/// Fails the current tenant's inspections that have been Running for longer than
/// <c>Inspections:RunningTimeout</c>, with <c>timed_out</c>. Returns the changes.
/// </summary>
public sealed record TimeOutInspectionsCommand : ICommand<IReadOnlyList<InspectionChange>>;
