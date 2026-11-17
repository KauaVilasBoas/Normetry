using Normetry.Application.Usage;

namespace Normetry.Application.Inspections;

/// <summary>
/// The <c>Inspections</c> settings, validated when the API starts. A tenant's plan comes from the
/// <c>Chat</c> section (<see cref="ChatOptions.PlanOf"/>), so one plan sets both quotas.
/// </summary>
public sealed class InspectionsOptions
{
    public const string Section = "Inspections";

    /// <summary>The daily quota of a plan that <see cref="DailyQuota"/> does not list.</summary>
    public const int DefaultDailyQuota = 5;

    /// <summary>Plan name to the inspections a tenant may start per UTC day; null means no limit.</summary>
    public Dictionary<string, int?> DailyQuota { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The most tokens one inspection may use, sent in <c>InspectionRequested</c>.</summary>
    public int TokenCeiling { get; init; } = 300_000;

    /// <summary>How long an inspection may stay Running before it fails with <c>timed_out</c>.</summary>
    public TimeSpan RunningTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>The plan's daily quota; null means no limit.</summary>
    public int? DailyQuotaOf(string plan) =>
        DailyQuota.TryGetValue(plan, out var quota) ? quota : DefaultDailyQuota;

    /// <summary>Throws on a plan <paramref name="chat"/> does not define, a quota below 1, a ceiling below 1 or a timeout that is not positive.</summary>
    public void Validate(ChatOptions chat)
    {
        foreach (var (plan, quota) in DailyQuota)
        {
            if (!chat.Plans.ContainsKey(plan))
            {
                throw new InvalidOperationException(
                    $"{Section}:DailyQuota:{plan} is not a plan in {ChatOptions.Section}:Plans."
                );
            }
            if (quota is < 1)
            {
                throw new InvalidOperationException(
                    $"{Section}:DailyQuota:{plan} must be 1 or more, or null for no limit."
                );
            }
        }
        if (TokenCeiling < 1)
        {
            throw new InvalidOperationException(
                $"{Section}:{nameof(TokenCeiling)} must be at least 1."
            );
        }
        if (RunningTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{Section}:{nameof(RunningTimeout)} must be positive."
            );
        }
    }
}
