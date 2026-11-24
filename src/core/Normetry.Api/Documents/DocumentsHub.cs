using Microsoft.AspNetCore.SignalR;
using Normetry.Api.Auth;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
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
    public const string Path = $"{HubPaths.Prefix}/documents";

    /// <summary>The client method that receives a <see cref="DocumentStatusChange"/>.</summary>
    public const string StatusChanged = "DocumentStatusChanged";

    /// <summary>
    /// Leaves the previous establishment's group and joins this one's. The establishment is loaded
    /// under the caller's tenant (RLS), so another tenant's id is refused like an unknown one.
    /// </summary>
    [HasPermission(Permissions.Documents.Read)]
    public Task JoinEstablishment(Guid establishmentId) =>
        EstablishmentGroups.JoinAsync(this, dispatcher, tenant, establishmentId);
}

/// <summary>
/// Pushes to the establishment's group on this instance.
/// ponytail: no backplane, so with several API instances a client hears only the changes its own
/// instance applied (it still sees every change on its next refetch); add the Redis backplane then.
/// </summary>
internal sealed class SignalRDocumentStatusNotifier(IHubContext<DocumentsHub> hub)
    : IChangeNotifier<DocumentStatusChange>
{
    public Task NotifyAsync(
        Guid tenantId,
        DocumentStatusChange change,
        CancellationToken cancellationToken
    ) =>
        hub
            .Clients.Group(EstablishmentGroups.Name(tenantId, change.EstablishmentId))
            .SendAsync(DocumentsHub.StatusChanged, change, cancellationToken);
}
