using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Establishments;
using Normetry.Application.Usage;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.StartInspection;

internal sealed class StartInspectionHandler(
    IEstablishmentRepository establishments,
    IDocumentRepository documents,
    IInspectionRepository inspections,
    IOutbox outbox,
    ICurrentUser currentUser,
    ITenantContext tenant,
    InspectionsOptions options,
    ChatOptions plans,
    TimeProvider clock
) : ICommandHandler<StartInspectionCommand, StartedInspectionResponse>
{
    public async Task<Result<StartedInspectionResponse>> HandleAsync(
        StartInspectionCommand request,
        CancellationToken cancellationToken
    )
    {
        var found = await establishments.FindOrNotFoundAsync(
            request.EstablishmentId,
            cancellationToken
        );
        if (!found.IsSuccess)
        {
            return found.Error;
        }
        var establishment = found.Value;
        // A concurrent start that passes this check ends at the partial unique index, which
        // TransactionBehavior turns into the same refusal.
        if (await inspections.HasInFlightAsync(establishment.Id, cancellationToken))
        {
            return InspectionErrors.Running;
        }
        if (!await documents.HasIndexedAsync(establishment.Id, cancellationToken))
        {
            return InspectionErrors.NoIndexedDocuments;
        }

        var now = clock.GetUtcNow();
        if (
            await inspections.TryCountStartAsync(
                tenant.TenantId,
                DateOnly.FromDateTime(now.UtcDateTime),
                options.DailyQuotaOf(plans.PlanOf(tenant.TenantId)),
                cancellationToken
            )
            is null
        )
        {
            return InspectionErrors.QuotaExceeded;
        }
        // The lock holds the tenant's other starts until this one commits, so they count one at a
        // time and see each other's inspections, also on either side of a UTC midnight (the day
        // count's row lock covers only one day). A Requested inspection older than the timeout
        // lost its message and does not count. A refusal rolls the day count back.
        await inspections.LockStartsAsync(tenant.TenantId, cancellationToken);
        if (
            await inspections.CountInFlightAsync(now - options.RunningTimeout, cancellationToken)
            >= options.MaxInFlightPerTenant
        )
        {
            return InspectionErrors.Capacity;
        }

        var inspection = Inspection.Request(
            tenant.TenantId,
            establishment.Id,
            currentUser.UserId,
            now
        );
        inspections.Add(inspection);
        await outbox.EnqueueAsync(
            new InspectionRequested(
                establishment.Id,
                inspection.Id,
                establishment.Jurisdiction,
                establishment.Type,
                options.TokenCeiling,
                currentUser.UserId
            ),
            cancellationToken
        );
        return new StartedInspectionResponse(inspection.Id);
    }
}
