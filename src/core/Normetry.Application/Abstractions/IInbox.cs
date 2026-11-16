namespace Normetry.Application.Abstractions;

/// <summary>
/// The messages the core has consumed, per tenant (<c>core.inbox_messages</c>). Recording one
/// is part of the transaction that applies it, so a redelivered message is applied once.
/// </summary>
public interface IInbox
{
    /// <summary>Records the message for the current tenant; false when it was already recorded.</summary>
    Task<bool> TryAcceptAsync(Guid messageId, string type, CancellationToken cancellationToken);
}
