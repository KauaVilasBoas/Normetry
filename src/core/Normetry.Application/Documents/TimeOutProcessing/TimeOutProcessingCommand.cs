using Normetry.BuildingBlocks.Cqrs;

namespace Normetry.Application.Documents.TimeOutProcessing;

/// <summary>
/// Fails the current tenant's versions that have been Processing for longer than
/// <c>Documents:ProcessingTimeout</c>, with <c>timed_out</c>. Returns the changes.
/// </summary>
public sealed record TimeOutProcessingCommand : ICommand<IReadOnlyList<DocumentStatusChange>>;
