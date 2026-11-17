namespace Normetry.Domain.Usage;

/// <summary>
/// How many inspections one tenant started on one UTC day. Rows are created and incremented only
/// by an atomic upsert in the database, like <see cref="ChatMessageCount"/>.
/// </summary>
public sealed class InspectionCount
{
    private InspectionCount(Guid tenantId, DateOnly day, int count)
    {
        TenantId = tenantId;
        Day = day;
        Count = count;
    }

    public Guid TenantId { get; private set; }

    /// <summary>The UTC day the inspections count in.</summary>
    public DateOnly Day { get; private set; }

    public int Count { get; private set; }
}
