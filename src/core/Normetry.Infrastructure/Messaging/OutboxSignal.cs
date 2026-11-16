using System.Threading.Channels;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// The tenants whose outbox messages this process just committed, so the dispatcher publishes them
/// at once instead of polling every tenant.
/// </summary>
internal sealed class OutboxSignal
{
    private readonly Channel<Guid> tenants = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true }
    );

    public void Notify(Guid tenantId) => tenants.Writer.TryWrite(tenantId);

    public ChannelReader<Guid> Reader => tenants.Reader;
}
