namespace Normetry.Application.Usage;

/// <summary>
/// Chat plans and which tenant is on which, from the <c>Chat</c> configuration section. Changing a
/// limit or a tenant's plan is a configuration change and a restart.
/// </summary>
public sealed class ChatOptions
{
    public const string Section = "Chat";

    /// <summary>The plan of every tenant without an entry in <see cref="TenantPlans"/>.</summary>
    public string DefaultPlan { get; init; } = "";

    public Dictionary<string, ChatPlan> Plans { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tenant id (UUID) to plan name.</summary>
    public Dictionary<string, string> TenantPlans { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tenant's daily limit per user; <c>null</c> means no limit.</summary>
    public int? DailyMessagesPerUser(Guid tenantId) =>
        Plans[TenantPlans.GetValueOrDefault(tenantId.ToString(), DefaultPlan)].DailyMessagesPerUser;

    /// <summary>Throws when a plan name is unknown, a tenant id is not a UUID or a limit is below 1.</summary>
    public void Validate()
    {
        foreach (var (name, plan) in Plans)
        {
            if (plan.DailyMessagesPerUser is < 1)
            {
                throw new InvalidOperationException(
                    $"{Section}:Plans:{name}:DailyMessagesPerUser must be 1 or more, or unset for no limit."
                );
            }
        }
        if (!Plans.ContainsKey(DefaultPlan))
        {
            throw new InvalidOperationException(
                $"{Section}:DefaultPlan '{DefaultPlan}' is not a plan in {Section}:Plans."
            );
        }
        foreach (var (tenant, plan) in TenantPlans)
        {
            if (!Guid.TryParseExact(tenant, "D", out _))
            {
                throw new InvalidOperationException(
                    $"{Section}:TenantPlans:{tenant} is not a tenant id (a UUID)."
                );
            }
            if (!Plans.ContainsKey(plan))
            {
                throw new InvalidOperationException(
                    $"{Section}:TenantPlans:{tenant} names '{plan}', which is not a plan in {Section}:Plans."
                );
            }
        }
    }
}

public sealed class ChatPlan
{
    /// <summary>Chat messages each user may send per UTC day; <c>null</c> means no limit.</summary>
    public int? DailyMessagesPerUser { get; init; }
}
