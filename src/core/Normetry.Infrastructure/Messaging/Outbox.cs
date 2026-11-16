using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Normetry.Application.Abstractions;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Adds the message to the request's unit of work, so <c>TransactionBehavior</c> saves it with
/// the state change, and records the tenant for the dispatcher. The tenant comes from the scope's
/// <see cref="ITenantContext"/>, and RLS refuses a row for any other.
/// </summary>
internal sealed class Outbox(CoreDbContext db, ITenantContext tenant, TimeProvider clock) : IOutbox
{
    internal const int TraceStateMaxLength = 512;

    public async Task EnqueueAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : IIntegrationMessage
    {
        var tenantId = tenant.TenantId;
        var id = Guid.CreateVersion7();
        var now = clock.GetUtcNow();
        // The request's span: the dispatcher continues its trace when it publishes.
        var activity = Activity.Current is { IdFormat: ActivityIdFormat.W3C } current
            ? current
            : null;
        db.OutboxMessages.Add(
            new OutboxMessage
            {
                TenantId = tenantId,
                Id = id,
                Type = TMessage.Type,
                RoutingKey = TMessage.RoutingKey,
                Body = MessageJson.Serialize(id, tenantId, now, message),
                TraceParent = activity?.Id,
                // A client may send any tracestate; an oversized one is dropped, never stored.
                TraceState = activity?.TraceStateString is { Length: <= TraceStateMaxLength } state
                    ? state
                    : null,
                CreatedAt = now,
            }
        );
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO core.outbox_tenants (tenant_id) VALUES ({tenantId}) ON CONFLICT DO NOTHING",
            cancellationToken
        );
    }
}
