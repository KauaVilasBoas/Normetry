using Normetry.Domain.Identity;

namespace Normetry.Domain.Usage;

/// <summary>
/// How many chat messages one user of a tenant sent on one UTC day. Rows are created and
/// incremented only by an atomic upsert in the database, so concurrent messages never lose a count.
/// </summary>
public sealed class ChatMessageCount
{
    /// <summary>The user is a <c>sub</c>, so it has the same limit.</summary>
    public const int UserIdMaxLength = User.SubMaxLength;

    private ChatMessageCount(Guid tenantId, string userId, DateOnly day, int count)
    {
        TenantId = tenantId;
        UserId = userId;
        Day = day;
        Count = count;
    }

    public Guid TenantId { get; private set; }

    /// <summary>The identity provider's subject id (<c>sub</c>).</summary>
    public string UserId { get; private set; }

    /// <summary>The UTC day the messages count in.</summary>
    public DateOnly Day { get; private set; }

    public int Count { get; private set; }
}
