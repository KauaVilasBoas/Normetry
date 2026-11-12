using Normetry.Domain.Establishments;
using Normetry.Domain.Tenancy;
using Xunit;

namespace Normetry.UnitTests.Domain;

public sealed class EstablishmentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_trims_the_name_and_generates_a_time_ordered_id()
    {
        var establishment = Establishment.Create(
            TenantId,
            "  Bistro Centro ",
            EstablishmentType.Restaurant,
            Jurisdiction.BR
        );

        Assert.Equal("Bistro Centro", establishment.Name);
        Assert.Equal(TenantId, establishment.TenantId);
        Assert.Equal(7, establishment.Id.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_rejected(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            Establishment.Create(TenantId, name!, EstablishmentType.Clinic, Jurisdiction.US)
        );
        Assert.Throws<ArgumentException>(() => Tenant.Create(TenantId, name!));
    }

    [Fact]
    public void A_name_over_the_maximum_length_is_rejected()
    {
        var longest = new string('a', Establishment.NameMaxLength);

        Establishment.Create(TenantId, longest, EstablishmentType.Pharmacy, Jurisdiction.EU);
        Assert.Throws<ArgumentException>(() =>
            Establishment.Create(
                TenantId,
                longest + "a",
                EstablishmentType.Pharmacy,
                Jurisdiction.EU
            )
        );
        Assert.Throws<ArgumentException>(() =>
            Tenant.Create(TenantId, new string('a', Tenant.NameMaxLength + 1))
        );
    }

    [Fact]
    public void An_empty_tenant_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Establishment.Create(
                Guid.Empty,
                "Kitchen",
                EstablishmentType.Restaurant,
                Jurisdiction.US
            )
        );
        Assert.Throws<ArgumentException>(() => Tenant.Create(Guid.Empty, "Demo"));
    }

    [Fact]
    public void An_undefined_type_or_jurisdiction_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Establishment.Create(TenantId, "Kitchen", default, Jurisdiction.US)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Establishment.Create(
                TenantId,
                "Kitchen",
                EstablishmentType.Restaurant,
                (Jurisdiction)99
            )
        );
    }
}
