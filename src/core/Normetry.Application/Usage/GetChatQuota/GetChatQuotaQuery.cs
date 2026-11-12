using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Usage.GetChatQuota;

/// <summary>The caller's chat quota for today, so the web app can show it before sending.</summary>
public sealed record GetChatQuotaQuery : IQuery<ChatQuotaResponse>;
