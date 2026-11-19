using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Establishments;
using Normetry.Application.Usage;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.BuildingBlocks.Time;
using Normetry.Domain.Establishments;
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
        var notStartable = await RefuseWhenRunningOrWithoutIndexedDocumentsAsync(
            establishment.Id,
            cancellationToken
        );
        if (notStartable is not null)
        {
            return notStartable;
        }
        var now = clock.GetUtcNow();
        var overQuota = await CountStartOrRefuseOverDailyQuotaAsync(now, cancellationToken);
        if (overQuota is not null)
        {
            return overQuota;
        }
        var atCapacity = await RefuseWhenTenantIsAtCapacityAsync(now, cancellationToken);
        if (atCapacity is not null)
        {
            return atCapacity;
        }
        var inspection = await RequestInspectionAsync(establishment, now, cancellationToken);
        return new StartedInspectionResponse(inspection.Id);
    }

    private async Task<Error?> RefuseWhenRunningOrWithoutIndexedDocumentsAsync(
        Guid establishmentId,
        CancellationToken cancellationToken
    )
    {
        // A concurrent start that passes this check ends at the partial unique index, which
        // TransactionBehavior turns into the same refusal.
        if (await inspections.HasInFlightAsync(establishmentId, cancellationToken))
        {
            return InspectionErrors.Running;
        }
        return await documents.HasIndexedAsync(establishmentId, cancellationToken)
            ? null
            : InspectionErrors.NoIndexedDocuments;
    }

    /// <summary>Counts the start against the tenant's daily quota, or refuses when it is spent.</summary>
    private async Task<Error?> CountStartOrRefuseOverDailyQuotaAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var counted = await inspections.TryCountStartAsync(
            tenant.TenantId,
            now.UtcDate(),
            options.DailyQuotaOf(plans.PlanOf(tenant.TenantId)),
            cancellationToken
        );
        return counted is null ? InspectionErrors.QuotaExceeded : null;
    }

    private async Task<Error?> RefuseWhenTenantIsAtCapacityAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        // The lock holds the tenant's other starts until this one commits, so they count one at a
        // time and see each other's inspections, also on either side of a UTC midnight (the day
        // count's row lock covers only one day). A Requested inspection older than the timeout
        // lost its message and does not count. A refusal rolls the day count back.
        await inspections.LockStartsAsync(tenant.TenantId, cancellationToken);
        var inFlight = await inspections.CountInFlightAsync(
            now - options.RunningTimeout,
            cancellationToken
        );
        return inFlight >= options.MaxInFlightPerTenant ? InspectionErrors.Capacity : null;
    }

    /// <summary>The Requested inspection and its <c>InspectionRequested</c>, committed together.</summary>
    private async Task<Inspection> RequestInspectionAsync(
        Establishment establishment,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
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
        return inspection;
    }
}
