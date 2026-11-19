using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Abstractions;

/// <summary>
/// Records the message in the inbox, then applies it, so a redelivered message changes nothing.
/// Both run inside the handler, so inside the transaction <c>TransactionBehavior</c> opens around
/// it: the inbox row commits only with the change it guards.
/// </summary>
internal abstract class InboxCommandHandler<TCommand, TChange>(IInbox inbox)
    : ICommandHandler<TCommand, TChange?>
    where TCommand : IInboxCommand<TChange>
    where TChange : class
{
    /// <summary>The result of a message that changed nothing.</summary>
    protected static readonly Result<TChange?> Unchanged = Result<TChange?>.Success(null);

    public async Task<Result<TChange?>> HandleAsync(
        TCommand request,
        CancellationToken cancellationToken
    )
    {
        var firstDelivery = await inbox.TryAcceptAsync(
            request.MessageId,
            request.MessageType,
            cancellationToken
        );
        return firstDelivery ? await ApplyAsync(request, cancellationToken) : Unchanged;
    }

    /// <summary>Applies a message the inbox had not recorded yet.</summary>
    protected abstract Task<Result<TChange?>> ApplyAsync(
        TCommand request,
        CancellationToken cancellationToken
    );
}
