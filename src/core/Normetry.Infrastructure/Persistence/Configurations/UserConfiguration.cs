using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Normetry.Domain.Identity;
using Normetry.Domain.Tenancy;

namespace Normetry.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        // Same shape as profiles: (tenant_id, id), referenced by the links.
        builder.HasKey(user => new { user.TenantId, user.Id });
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.Sub).HasMaxLength(User.SubMaxLength);
        // How a request finds its caller, and the conflict target of the first sign-in.
        builder.HasIndex(user => new { user.TenantId, user.Sub }).IsUnique();

        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(user => user.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasMany(user => user.Profiles)
            .WithOne()
            .HasForeignKey(held => new { held.TenantId, held.UserId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");
        builder.HasKey(held => new
        {
            held.TenantId,
            held.UserId,
            held.ProfileId,
        });
        // Deleting a profile unassigns it from everyone.
        builder
            .HasOne<Profile>()
            .WithMany()
            .HasForeignKey(held => new { held.TenantId, held.ProfileId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
