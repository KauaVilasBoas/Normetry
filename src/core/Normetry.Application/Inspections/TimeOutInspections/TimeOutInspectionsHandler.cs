using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Inspections.TimeOutInspections;

internal sealed class TimeOutInspectionsHandler(
    IInspectionRepository inspections,
    InspectionsOptions options,
    TimeProvider clock
) : ICommandHandler<TimeOutInspectionsCommand, IReadOnlyList<InspectionChange>>
{
    public async Task<Result<IReadOnlyList<InspectionChange>>> HandleAsync(
        TimeOutInspectionsCommand request,
        CancellationToken cancellationToken
    )
    {
        var now = clock.GetUtcNow();
        var changes = new List<InspectionChange>();
        foreach (
            var inspection in await inspections.ListInFlightSinceForUpdateAsync(
                now - options.RunningTimeout,
                cancellationToken
            )
        )
        {
            if (inspection.TimeOut(options.RunningTimeout, now))
            {
                changes.Add(InspectionChange.From(inspection));
            }
        }
        return changes;
    }
}
