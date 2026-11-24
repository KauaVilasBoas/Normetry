using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Normetry.Domain.Identity;
using Normetry.Domain.Tenancy;

namespace Normetry.Infrastructure.Persistence.Configurations;

internal sealed class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    /// <summary>One name per tenant; a concurrent create or rename ends here.</summary>
    internal const string NameIndex = "ix_profiles_tenant_id_name";

    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.ToTable("profiles", table =>
        {
            table.HasCheckConstraint(
                "ck_profiles_system_key",
                $"system_key IN ({SqlLiterals.List(Profile.SystemKeys.All)})"
            );
            // Owner, and only Owner, grants every permission.
            table.HasCheckConstraint(
                "ck_profiles_grants_all",
                $"grants_all = (coalesce(system_key, '') = {SqlLiterals.Quote(Profile.SystemKeys.Owner)})"
            );
        });
        // The key leads with tenant_id like every tenant-scoped lookup (the RLS predicate
        // included), and the links reference (tenant_id, id): a link can never join a profile of
        // one tenant to a user of another, even by id.
        builder.HasKey(profile => new { profile.TenantId, profile.Id });
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.Property(profile => profile.Name).HasMaxLength(Profile.NameMaxLength);
        builder.Property(profile => profile.SystemKey).HasMaxLength(20);
        builder
            .HasIndex(profile => new { profile.TenantId, profile.Name })
            .IsUnique()
            .HasDatabaseName(NameIndex);
        // One profile per system key and tenant, so a tenant has exactly one Owner.
        builder
            .HasIndex(profile => new { profile.TenantId, profile.SystemKey })
            .IsUnique()
            .HasFilter("system_key IS NOT NULL");

        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(profile => profile.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasMany(profile => profile.GrantedPermissions)
            .WithOne()
            .HasForeignKey(granted => new { granted.TenantId, granted.ProfileId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ProfilePermissionConfiguration : IEntityTypeConfiguration<ProfilePermission>
{
    public void Configure(EntityTypeBuilder<ProfilePermission> builder)
    {
        builder.ToTable("profile_permissions");
        builder.HasKey(granted => new
        {
            granted.TenantId,
            granted.ProfileId,
            granted.Permission,
        });
        builder.Property(granted => granted.Permission).HasMaxLength(Permissions.NameMaxLength);
        builder
            .HasOne<Permission>()
            .WithMany()
            .HasForeignKey(granted => granted.Permission)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
