using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Inspections.GetInspection;

/// <summary>One inspection of the establishment, with its findings and their action items.</summary>
public sealed record GetInspectionQuery(Guid EstablishmentId, Guid Id) : IQuery<InspectionResponse>;
