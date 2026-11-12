using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Usage.CountChatMessage;

/// <summary>
/// Counts one chat message of the caller against the tenant plan's daily limit, before the message
/// is forwarded to the AI service. Fails with an <c>ErrorType.RateLimited</c> error (HTTP 429)
/// when the limit is reached; nothing is counted then.
/// </summary>
public sealed record CountChatMessageCommand : ICommand<ChatQuotaResponse>;
