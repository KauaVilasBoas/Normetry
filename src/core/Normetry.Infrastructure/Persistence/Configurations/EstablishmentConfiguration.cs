using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Normetry.Domain.Establishments;
using Normetry.Domain.Tenancy;

namespace Normetry.Infrastructure.Persistence.Configurations;

internal sealed class EstablishmentConfiguration : IEntityTypeConfiguration<Establishment>
{
    public void Configure(EntityTypeBuilder<Establishment> builder)
    {
        // Enums are stored by name. The checks list the enum names, so a new value shows up as a
        // model change in the next migration.
        builder.ToTable("establishments", table =>
        {
            table.HasCheckConstraint(
                "ck_establishments_type",
                $"type IN ({Names<EstablishmentType>()})"
            );
            table.HasCheckConstraint(
                "ck_establishments_jurisdiction",
                $"jurisdiction IN ({Names<Jurisdiction>()})"
            );
        });
        builder.Property(establishment => establishment.Id).ValueGeneratedNever();
        builder
            .Property(establishment => establishment.Name)
            .HasMaxLength(Establishment.NameMaxLength);
        builder.Property(establishment => establishment.Type).HasConversion<string>();
        builder.Property(establishment => establishment.Jurisdiction).HasConversion<string>();

        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(establishment => establishment.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        // Leads with tenant_id like every tenant-scoped lookup (the RLS predicate included),
        // serves the list ordered by name and indexes the foreign key.
        builder.HasIndex(establishment => new { establishment.TenantId, establishment.Name });
    }

    private static string Names<TEnum>()
        where TEnum : struct, Enum =>
        string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"));
}
