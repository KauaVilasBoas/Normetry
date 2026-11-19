namespace Normetry.Infrastructure.Messaging;

/// <summary>What a status consumer does with a delivery once its handler returns.</summary>
internal enum ConsumeOutcome
{
    Ack,

    /// <summary>Wait, then reject with requeue: the broker counts the delivery.</summary>
    Retry,

    /// <summary>Reject without requeue, which dead-letters the message at once.</summary>
    DeadLetter,
}
