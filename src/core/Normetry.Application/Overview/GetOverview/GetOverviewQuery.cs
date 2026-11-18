using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Overview.GetOverview;

/// <summary>
/// The establishment's overview, with only the sections the caller's permissions cover:
/// <c>inspections.read</c> for the inspection sections, <c>documents.read</c> for the documents.
/// </summary>
public sealed record GetOverviewQuery(Guid EstablishmentId) : IQuery<OverviewResponse>;
