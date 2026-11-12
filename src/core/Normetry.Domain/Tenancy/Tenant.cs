namespace Normetry.Domain.Tenancy;

/// <summary>
/// A customer organization. Its id is the <c>tenant_id</c> claim the identity provider puts in
/// every access token, so it is given, not generated.
/// </summary>
public sealed class Tenant
{
    public const int NameMaxLength = 200;

    private Tenant(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public static Tenant Create(Guid id, string name) =>
        new(Names.RequiredId(id, nameof(id)), Names.Required(name, NameMaxLength, nameof(name)));
}
