using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Establishments;

namespace Normetry.Application.Establishments.CreateEstablishment;

internal sealed class CreateEstablishmentHandler(
    IEstablishmentRepository establishments,
    ITenantContext tenant
) : ICommandHandler<CreateEstablishmentCommand, Guid>
{
    public Task<Result<Guid>> HandleAsync(
        CreateEstablishmentCommand request,
        CancellationToken cancellationToken
    )
    {
        var establishment = Establishment.Create(
            tenant.TenantId,
            request.Name,
            request.Type,
            request.Jurisdiction
        );
        establishments.Add(establishment);
        return Task.FromResult<Result<Guid>>(establishment.Id);
    }
}
