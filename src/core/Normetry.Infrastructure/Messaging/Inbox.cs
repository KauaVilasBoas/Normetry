using Microsoft.EntityFrameworkCore;
using Normetry.Application.Abstractions;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// One statement in the handler's transaction. A concurrent delivery of the same message waits for
/// the first to commit, then inserts nothing.
/// </summary>
internal sealed class Inbox(CoreDbContext db, ITenantContext tenant, TimeProvider clock) : IInbox
{
    public async Task<bool> TryAcceptAsync(
        Guid messageId,
        string type,
        CancellationToken cancellationToken
    ) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO core.inbox_messages (tenant_id, message_id, type, received_at)
            VALUES ({tenant.TenantId}, {messageId}, {type}, {clock.GetUtcNow()})
            ON CONFLICT (tenant_id, message_id) DO NOTHING
            """,
            cancellationToken
        ) == 1;
}
