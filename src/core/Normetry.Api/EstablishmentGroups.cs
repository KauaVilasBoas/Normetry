using Microsoft.AspNetCore.SignalR;
using Normetry.Application.Abstractions;
using Normetry.Application.Establishments.GetEstablishment;
using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Api;

/// <summary>
/// What the hubs that push one establishment's changes share: a connection follows one
/// establishment at a time, in a group named by tenant and establishment. Each hub has its own
/// groups, so the same name in two hubs never mixes their pushes.
/// </summary>
internal static class EstablishmentGroups
{
    private const string GroupItem = "establishment.group";

    /// <summary>
    /// Leaves the previous establishment's group and joins this one's. The establishment is loaded
    /// under the caller's tenant (RLS), so another tenant's id is refused like an unknown one, with
    /// a <see cref="HubException"/> carrying <c>establishment.not_found</c>.
    /// </summary>
    public static async Task JoinAsync(
        Hub hub,
        IDispatcher dispatcher,
        ITenantContext tenant,
        Guid establishmentId
    )
    {
        var context = hub.Context;
        var establishment = await dispatcher.QueryAsync(
            new GetEstablishmentQuery(establishmentId),
            context.ConnectionAborted
        );
        if (!establishment.IsSuccess)
        {
            throw new HubException(establishment.Error.Code);
        }

        var group = Name(tenant.TenantId, establishmentId);
        if (context.Items.TryGetValue(GroupItem, out var previous) && previous is string left)
        {
            await hub.Groups.RemoveFromGroupAsync(
                context.ConnectionId,
                left,
                context.ConnectionAborted
            );
        }
        await hub.Groups.AddToGroupAsync(context.ConnectionId, group, context.ConnectionAborted);
        context.Items[GroupItem] = group;
    }

    /// <summary>The tenant is part of the name, so a group never spans two tenants, even by id.</summary>
    public static string Name(Guid tenantId, Guid establishmentId) =>
        $"tenants/{tenantId}/establishments/{establishmentId}";
}
