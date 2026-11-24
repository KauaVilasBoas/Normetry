using Microsoft.AspNetCore.SignalR;
using Normetry.Api.Auth;
using Normetry.Application.Abstractions;
using Normetry.Application.Inspections;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Identity;

namespace Normetry.Api.Inspections;

/// <summary>
/// Live inspection progress at <c>/hubs/inspections</c>, the same shape as <c>/hubs/documents</c>:
/// WebSockets only, the access token in the <c>access_token</c> query parameter,
/// <c>inspections.read</c> to connect and on each call. After <see cref="JoinEstablishment"/> a
/// connection receives <see cref="Changed"/> with an <see cref="InspectionChange"/> for each
/// inspection of that establishment that moved.
/// </summary>
[HasPermission(Permissions.Inspections.Read)]
public sealed class InspectionsHub(IDispatcher dispatcher, ITenantContext tenant) : Hub
{
    public const string Path = "/hubs/inspections";

    /// <summary>The client method that receives an <see cref="InspectionChange"/>.</summary>
    public const string Changed = "InspectionChanged";

    /// <summary>
    /// Leaves the previous establishment's group and joins this one's. The establishment is loaded
    /// under the caller's tenant (RLS), so another tenant's id is refused like an unknown one.
    /// </summary>
    [HasPermission(Permissions.Inspections.Read)]
    public Task JoinEstablishment(Guid establishmentId) =>
        EstablishmentGroups.JoinAsync(this, dispatcher, tenant, establishmentId);
}

/// <summary>
/// Pushes to the establishment's group on this instance.
/// ponytail: no backplane, like the documents hub; add the Redis backplane with several instances.
/// </summary>
internal sealed class SignalRInspectionNotifier(IHubContext<InspectionsHub> hub)
    : IChangeNotifier<InspectionChange>
{
    public Task NotifyAsync(
        Guid tenantId,
        InspectionChange change,
        CancellationToken cancellationToken
    ) =>
        hub
            .Clients.Group(EstablishmentGroups.Name(tenantId, change.EstablishmentId))
            .SendAsync(InspectionsHub.Changed, change, cancellationToken);
}
