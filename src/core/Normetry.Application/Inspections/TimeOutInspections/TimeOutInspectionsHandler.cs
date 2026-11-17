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
        return (
            await inspections.ListRunningSinceForUpdateAsync(
                now - options.RunningTimeout,
                cancellationToken
            )
        )
            .Where(inspection => inspection.TimeOut(options.RunningTimeout, now))
            .Select(InspectionChange.From)
            .ToArray();
    }
}
