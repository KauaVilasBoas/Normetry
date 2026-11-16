using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Normetry.Domain.Identity;

namespace Normetry.Infrastructure.Persistence.Configurations;

/// <summary>The global catalog: no tenant and no RLS, filled by a data migration, read-only for app_core.</summary>
internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(permission => permission.Name);
        builder.Property(permission => permission.Name).HasMaxLength(Permissions.NameMaxLength);
    }
}
