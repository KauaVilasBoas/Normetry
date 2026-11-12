using Microsoft.EntityFrameworkCore;
using Normetry.Application.Usage;

namespace Normetry.Infrastructure.Persistence;

// No tenant filter here: row-level security returns and accepts only the transaction's tenant.
internal sealed class ChatMessageCounter(CoreDbContext db) : IChatMessageCounter
{
    public async Task<int?> TryIncrementAsync(
        Guid tenantId,
        string userId,
        DateOnly day,
        int? limit,
        CancellationToken cancellationToken
    )
    {
        // One statement: the first message of the day inserts the row, later ones update it under
        // the row lock ON CONFLICT takes, and the WHERE re-checks the latest count, so concurrent
        // requests never lose an update or pass the limit. No row comes back at the limit.
        var counts = await db
            .Database.SqlQuery<int>(
                $"""
                INSERT INTO core.chat_message_counts AS c (tenant_id, user_id, day, count)
                VALUES ({tenantId}, {userId}, {day}, 1)
                ON CONFLICT (tenant_id, user_id, day) DO UPDATE SET count = c.count + 1
                    WHERE {limit}::integer IS NULL OR c.count < {limit}::integer
                RETURNING c.count AS "Value"
                """
            )
            .ToListAsync(cancellationToken);
        return counts is [var count] ? count : null;
    }

    public Task<int> GetAsync(string userId, DateOnly day, CancellationToken cancellationToken) =>
        db
            .ChatMessageCounts.AsNoTracking()
            .Where(count => count.UserId == userId && count.Day == day)
            .Select(count => count.Count)
            .FirstOrDefaultAsync(cancellationToken);
}
