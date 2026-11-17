using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.SetActionItemStatus;

/// <summary>
/// Marks the action of a finding of a completed inspection of the establishment done, or reopens
/// it. Setting the status it already has changes nothing. Returns the finding with its action.
/// </summary>
public sealed record SetActionItemStatusCommand(
    Guid EstablishmentId,
    Guid InspectionId,
    Guid ActionItemId,
    ActionItemStatus Status
) : ICommand<FindingResponse>;
