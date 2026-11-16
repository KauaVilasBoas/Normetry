using Microsoft.AspNetCore.SignalR;
using Normetry.Api.Auth;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Establishments.GetEstablishment;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Identity;

namespace Normetry.Api.Documents;

/// <summary>
/// Live document status at <c>/hubs/documents</c> (WebSockets only; the access token goes in the
/// <c>access_token</c> query parameter). Connecting needs <c>documents.read</c> (the class
/// attribute), and so does each method (its own attribute: SignalR checks only those per call), so
/// a revoked permission stops new joins. A connection
/// follows one establishment at a time with <see cref="JoinEstablishment"/> and then receives
/// <see cref="StatusChanged"/> with a <see cref="DocumentStatusChange"/> for each version of it.
/// </summary>
[HasPermission(Permissions.Documents.Read)]
public sealed class DocumentsHub(IDispatcher dispatcher, ITenantContext tenant) : Hub
{
    public const string Path = "/hubs/documents";

    /// <summary>The client method that receives a <see cref="DocumentStatusChange"/>.</summary>
    public const string StatusChanged = "DocumentStatusChanged";

    private const string GroupItem = "documents.group";

    /// <summary>
    /// Leaves the previous establishment's group and joins this one's. The establishment is loaded
    /// under the caller's tenant (RLS), so another tenant's id is refused like an unknown one.
    /// </summary>
    [HasPermission(Permissions.Documents.Read)]
    public async Task JoinEstablishment(Guid establishmentId)
    {
        var establishment = await dispatcher.QueryAsync(
            new GetEstablishmentQuery(establishmentId),
            Context.ConnectionAborted
        );
        if (!establishment.IsSuccess)
        {
            throw new HubException(establishment.Error.Code);
        }

        var group = GroupName(tenant.TenantId, establishmentId);
        if (Context.Items.TryGetValue(GroupItem, out var previous) && previous is string left)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                left,
                Context.ConnectionAborted
            );
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, group, Context.ConnectionAborted);
        Context.Items[GroupItem] = group;
    }

    /// <summary>The tenant is part of the name, so a group never spans two tenants, even by id.</summary>
    public static string GroupName(Guid tenantId, Guid establishmentId) =>
        $"tenants/{tenantId}/establishments/{establishmentId}";
}

/// <summary>
/// Pushes to the establishment's group on this instance.
/// ponytail: no backplane, so with several API instances a client hears only the changes its own
/// instance applied (it still sees every change on its next refetch); add the Redis backplane then.
/// </summary>
internal sealed class SignalRDocumentStatusNotifier(IHubContext<DocumentsHub> hub)
    : IDocumentStatusNotifier
{
    public Task NotifyAsync(
        Guid tenantId,
        DocumentStatusChange change,
        CancellationToken cancellationToken
    ) =>
        hub
            .Clients.Group(DocumentsHub.GroupName(tenantId, change.EstablishmentId))
            .SendAsync(DocumentsHub.StatusChanged, change, cancellationToken);
}
