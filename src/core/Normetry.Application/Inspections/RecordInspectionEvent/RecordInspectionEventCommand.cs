using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Inspections.RecordInspectionEvent;

/// <summary>
/// Applies one of the AI service's inspection events to its inspection, once per message id.
/// Returns the change, or null when the event changed nothing (a redelivery, a late or lower
/// progress, an unknown inspection or ids that do not belong together). Runs in a scope whose
/// tenant is the message's.
/// </summary>
public sealed record RecordInspectionEventCommand(
    Guid MessageId,
    string MessageType,
    IInspectionEvent Event
) : ICommand<InspectionChange?>;
