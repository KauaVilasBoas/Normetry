namespace Normetry.Domain.Establishments;

/// <summary>A place a tenant runs and keeps compliant, such as one restaurant.</summary>
public sealed class Establishment
{
    public const int NameMaxLength = 200;

    private Establishment(
        Guid id,
        Guid tenantId,
        string name,
        EstablishmentType type,
        Jurisdiction jurisdiction
    )
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Type = type;
        Jurisdiction = jurisdiction;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; }

    public EstablishmentType Type { get; private set; }

    /// <summary>Whose regulations apply.</summary>
    public Jurisdiction Jurisdiction { get; private set; }

    /// <summary>
    /// A new establishment with an id generated here (UUIDv7, time-ordered, so inserts stay at
    /// the end of the primary key index). The id never comes from the client.
    /// </summary>
    public static Establishment Create(
        Guid tenantId,
        string name,
        EstablishmentType type,
        Jurisdiction jurisdiction
    )
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unknown establishment type."
            );
        }
        if (!Enum.IsDefined(jurisdiction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(jurisdiction),
                jurisdiction,
                "Unknown jurisdiction."
            );
        }

        return new(
            Guid.CreateVersion7(),
            Names.RequiredId(tenantId, nameof(tenantId)),
            Names.Required(name, NameMaxLength, nameof(name)),
            type,
            jurisdiction
        );
    }
}

// Values start at 1, so a missing field (0) is not a valid value.
public enum EstablishmentType
{
    Restaurant = 1,
    Pharmacy,
    Clinic,
}

public enum Jurisdiction
{
    /// <summary>United States: FDA Food Code.</summary>
    US = 1,

    /// <summary>European Union: Regulation (EC) 852/2004.</summary>
    EU,

    /// <summary>Brazil: Anvisa RDC 216/2004.</summary>
    BR,
}
