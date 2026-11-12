using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Usage.CountChatMessage;

internal sealed class CountChatMessageHandler(
    IChatMessageCounter counter,
    ChatOptions options,
    ITenantContext tenant,
    ICurrentUser user,
    TimeProvider clock
) : ICommandHandler<CountChatMessageCommand, ChatQuotaResponse>
{
    internal static readonly Error QuotaExceeded = new(
        "chat.quota_exceeded",
        "The daily chat message limit is reached. It resets at midnight UTC.",
        ErrorType.RateLimited
    );

    public async Task<Result<ChatQuotaResponse>> HandleAsync(
        CountChatMessageCommand request,
        CancellationToken cancellationToken
    )
    {
        var now = clock.GetUtcNow();
        var limit = options.DailyMessagesPerUser(tenant.TenantId);
        var count = await counter.TryIncrementAsync(
            tenant.TenantId,
            user.UserId,
            ChatQuotaResponse.Day(now),
            limit,
            cancellationToken
        );
        return count is { } used ? ChatQuotaResponse.Create(limit, used, now) : QuotaExceeded;
    }
}
