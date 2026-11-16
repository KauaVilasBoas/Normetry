using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Documents.RecordIngestion;

/// <summary>
/// Applies one of the AI service's ingestion events to its version, once per message id: Started
/// makes it Processing, Indexed makes it Indexed (also after a timeout), Failed makes it Failed.
/// Returns the change, or null when the event changed nothing (a redelivery, a late event or an
/// unknown version). Runs in a scope whose tenant is the message's.
/// </summary>
public sealed record RecordIngestionCommand(
    Guid MessageId,
    string MessageType,
    IDocumentVersionEvent Event
) : ICommand<DocumentStatusChange?>;
