using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Inspections.StartInspection;

/// <summary>
/// Starts a simulated inspection of one of the tenant's establishments: Requested, announced to
/// the AI service with <c>InspectionRequested</c> in the same transaction. Refused while another
/// one of the establishment is in flight, when it has no indexed document, past the tenant's daily
/// quota (counted only when the start succeeds) and while the tenant has
/// <see cref="InspectionsOptions.MaxInFlightPerTenant"/> in flight across its establishments.
/// </summary>
public sealed record StartInspectionCommand(Guid EstablishmentId)
    : ICommand<StartedInspectionResponse>;
