using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Normetry.Api.Auth;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Api.RateLimiting;

/// <summary>Named policies, for <c>[EnableRateLimiting]</c> and the YARP chat route.</summary>
public static class RateLimitPolicies
{
    /// <summary>A new document and a new version share one bucket.</summary>
    public const string Uploads = "uploads";

    public const string InspectionStart = "inspection-start";

    /// <summary>The YARP establishment route: conversations and their messages.</summary>
    public const string Chat = "chat";
}

/// <summary>
/// The <c>RateLimiting</c> settings, validated when the API starts: requests per minute for one
/// user of one tenant, per policy.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";

    /// <summary>Every request, hubs included.</summary>
    public int GlobalPerMinute { get; init; } = 120;

    public int UploadsPerMinute { get; init; } = 20;

    public int InspectionStartsPerMinute { get; init; } = 5;

    public int ChatPerMinute { get; init; } = 20;

    public void Validate()
    {
        foreach (
            var (name, value) in new[]
            {
                (nameof(GlobalPerMinute), GlobalPerMinute),
                (nameof(UploadsPerMinute), UploadsPerMinute),
                (nameof(InspectionStartsPerMinute), InspectionStartsPerMinute),
                (nameof(ChatPerMinute), ChatPerMinute),
            }
        )
        {
            if (value < 1)
            {
                throw new InvalidOperationException($"{Section}:{name} must be at least 1.");
            }
        }
    }
}

internal static class RateLimitingSetup
{
    private static readonly Error RateLimited = new(
        "rate_limited",
        "Too many requests. Try again later.",
        ErrorType.RateLimited
    );

    /// <summary>
    /// ASP.NET Core rate limiting, partitioned by tenant and user (<c>{tenant_id}:{sub}</c> of the
    /// validated token): a global limiter on every request plus the stricter
    /// <see cref="RateLimitPolicies"/>, each its own bucket. Every limit is a token bucket holding
    /// a minute's allowance, refilled one token at a time over the minute, so a burst may spend it
    /// at once and then waits for the refill; no request is queued. A refusal is the 429 problem
    /// <c>rate_limited</c> with <c>Retry-After</c>. Counters live in memory, per instance.
    /// </summary>
    public static WebApplicationBuilder AddRateLimiting(this WebApplicationBuilder builder)
    {
        var options = builder
            .Configuration.GetSection(RateLimitingOptions.Section)
            .Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();
        options.Validate();

        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                Partition(context, options.GlobalPerMinute)
            );
            limiter.AddPolicy(RateLimitPolicies.Uploads, context =>
                Partition(context, options.UploadsPerMinute)
            );
            limiter.AddPolicy(RateLimitPolicies.InspectionStart, context =>
                Partition(context, options.InspectionStartsPerMinute)
            );
            limiter.AddPolicy(RateLimitPolicies.Chat, context =>
                Partition(context, options.ChatPerMinute)
            );
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = (
                        (long)Math.Ceiling(retryAfter.TotalSeconds)
                    ).ToString(CultureInfo.InvariantCulture);
                }
                return new ValueTask(
                    Result.Failure(RateLimited).ToProblemDetails().ExecuteAsync(context.HttpContext)
                );
            };
        });
        return builder;
    }

    // A request without a valid tenant and sub is not limited: authorization refuses it (401 or
    // 403) before any handler or database work, a shared bucket would let anyone turn other
    // callers' refusals into 429s, and neither a client header nor the address is a key a tenant
    // cannot share or forge.
    private static RateLimitPartition<string> Partition(HttpContext context, int perMinute) =>
        Key(context.User) is { } key
            ? RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = perMinute,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1) / perMinute,
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter("");

    // The tenant id has a fixed form without ':', so two users never share a key.
    private static string? Key(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && HttpCurrentUser.TryGetTenantId(user, out var tenantId)
        && user.FindFirstValue("sub") is { Length: > 0 } sub
            ? $"{tenantId}:{sub}"
            : null;
}
