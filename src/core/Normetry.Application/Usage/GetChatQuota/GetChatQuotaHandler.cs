using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.BuildingBlocks.Time;

namespace Normetry.Application.Usage.GetChatQuota;

internal sealed class GetChatQuotaHandler(
    IChatMessageCounter counter,
    ChatOptions options,
    ITenantContext tenant,
    ICurrentUser user,
    TimeProvider clock
) : IQueryHandler<GetChatQuotaQuery, ChatQuotaResponse>
{
    public async Task<Result<ChatQuotaResponse>> HandleAsync(
        GetChatQuotaQuery request,
        CancellationToken cancellationToken
    )
    {
        var now = clock.GetUtcNow();
        var limit = options.DailyMessagesPerUser(tenant.TenantId);
        var used = limit is null
            ? 0
            : await counter.GetAsync(user.Sub, now.UtcDate(), cancellationToken);
        return ChatQuotaResponse.Create(limit, used, now);
    }
}
