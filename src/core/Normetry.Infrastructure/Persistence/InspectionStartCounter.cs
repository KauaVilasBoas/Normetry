using Microsoft.EntityFrameworkCore;
using Normetry.Application.Abstractions;
using Normetry.Application.Usage;

namespace Normetry.Infrastructure.Persistence;

// No tenant filter here: row-level security returns and accepts only the transaction's tenant.
// The row it inserts carries the scope's tenant, and RLS refuses any other.
internal sealed class InspectionStartCounter(CoreDbContext db, ITenantContext tenant)
    : IInspectionStartCounter
{
    public async Task<int?> TryCountStartAsync(
        DateOnly day,
        int? limit,
        CancellationToken cancellationToken
    )
    {
        // The chat counter's statement: the first start of the day inserts the row, later ones
        // update it under the row lock ON CONFLICT takes, and the WHERE re-checks the latest
        // count. No row comes back at the limit.
        var counts = await db
            .Database.SqlQuery<int>(
                $"""
                INSERT INTO core.inspection_counts AS c (tenant_id, day, count)
                VALUES ({tenant.TenantId}, {day}, 1)
                ON CONFLICT (tenant_id, day) DO UPDATE SET count = c.count + 1
                    WHERE {limit}::integer IS NULL OR c.count < {limit}::integer
                RETURNING c.count AS "Value"
                """
            )
            .ToListAsync(cancellationToken);
        return counts is [var count] ? count : null;
    }
}
