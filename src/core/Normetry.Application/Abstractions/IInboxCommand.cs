using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Abstractions;

/// <summary>
/// A command that applies one consumed queue message (<see cref="InboxCommandHandler{TCommand, TChange}"/>).
/// <see cref="MessageId"/> and <see cref="MessageType"/> are what the inbox records. Returns the
/// change, or null when the message changed nothing.
/// </summary>
public interface IInboxCommand<TChange> : ICommand<TChange?>
    where TChange : class
{
    Guid MessageId { get; }

    string MessageType { get; }
}
