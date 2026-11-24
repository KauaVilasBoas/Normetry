using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Normetry.Domain.Establishments;
using Normetry.Domain.Identity;
using Normetry.Domain.Inspections;
using Normetry.Domain.Tenancy;
using Normetry.Domain.Usage;

namespace Normetry.Infrastructure.Persistence.Configurations;

internal sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    /// <summary>One inspection in flight per establishment: a concurrent start ends here.</summary>
    internal const string InFlightIndex = "ix_inspections_tenant_id_establishment_id";

    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("inspections", table =>
            table.HasCheckConstraint(
                "ck_inspections_status",
                $"status IN ({SqlLiterals.List<InspectionStatus>()})"
            )
        );
        // Same shape as documents: (tenant_id, id), referenced by the findings.
        builder.HasKey(inspection => new { inspection.TenantId, inspection.Id });
        builder.Property(inspection => inspection.Id).ValueGeneratedNever();
        builder.Property(inspection => inspection.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(inspection => inspection.RequestedBy).HasMaxLength(User.SubMaxLength);
        builder.Property(inspection => inspection.CostUsd).HasPrecision(12, 6);
        builder
            .Property(inspection => inspection.FailureReasonCode)
            .HasMaxLength(Inspection.FailureCodeMaxLength);
        builder
            .Property(inspection => inspection.FailureReason)
            .HasMaxLength(Inspection.FailureReasonMaxLength);
        // Complex collections need an IList: the private lists are mapped, the read-only views are not.
        builder.Ignore(inspection => inspection.Themes);
        builder.ComplexCollection<List<ThemeProgress>, ThemeProgress>("themes", themes =>
            themes.ToJson()
        );

        // The composite key keeps an inspection of one tenant from pointing at another tenant's
        // establishment even by id: foreign key checks do not go through RLS.
        builder
            .HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(inspection => new { inspection.TenantId, inspection.EstablishmentId })
            .HasPrincipalKey(establishment => new { establishment.TenantId, establishment.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasMany(inspection => inspection.Findings)
            .WithOne()
            .HasForeignKey(finding => new { finding.TenantId, finding.InspectionId })
            .OnDelete(DeleteBehavior.Cascade);

        // The establishment's list, newest first (read backwards), and the foreign key.
        builder.HasIndex(inspection => new
        {
            inspection.TenantId,
            inspection.EstablishmentId,
            inspection.RequestedAt,
        });
        builder
            .HasIndex(inspection => new { inspection.TenantId, inspection.EstablishmentId })
            .IsUnique()
            .HasDatabaseName(InFlightIndex)
            .HasFilter(
                $"status IN ({SqlLiterals.List([nameof(InspectionStatus.Requested), nameof(InspectionStatus.Running)])})"
            );
    }
}

internal sealed class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("findings", table =>
        {
            table.HasCheckConstraint(
                "ck_findings_severity",
                $"severity IN ({SqlLiterals.List(Severities.InOrder)})"
            );
            table.HasCheckConstraint(
                "ck_findings_verdict",
                $"verdict IN ({SqlLiterals.List(Verdicts.All)})"
            );
            table.HasCheckConstraint(
                "ck_findings_confidence",
                $"confidence IN ({SqlLiterals.List(Confidences.All)})"
            );
            table.HasCheckConstraint(
                "ck_findings_review_status",
                $"review_status IN ({SqlLiterals.List<ReviewStatus>()})"
            );
        });
        builder.HasKey(finding => new { finding.TenantId, finding.Id });
        builder.Property(finding => finding.Id).ValueGeneratedNever();
        builder.Property(finding => finding.ItemId).HasMaxLength(Finding.ItemIdMaxLength);
        builder.Property(finding => finding.Theme).HasMaxLength(50);
        builder.Property(finding => finding.Title).HasMaxLength(Finding.TitleMaxLength);
        builder.Property(finding => finding.Severity).HasMaxLength(20);
        builder.Property(finding => finding.Verdict).HasMaxLength(30);
        builder.Property(finding => finding.Confidence).HasMaxLength(20);
        builder.Property(finding => finding.Rationale).HasMaxLength(Finding.RationaleMaxLength);
        builder.Property(finding => finding.ReviewStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(finding => finding.ReviewNote).HasMaxLength(Finding.ReviewNoteMaxLength);
        builder.Ignore(finding => finding.RegulationCitations);
        builder.Ignore(finding => finding.EvidenceCitations);
        builder.ComplexCollection<List<RegulationCitation>, RegulationCitation>(
            "regulationCitations",
            citations => citations.ToJson()
        );
        builder.ComplexCollection<List<EvidenceCitation>, EvidenceCitation>(
            "evidenceCitations",
            citations =>
            {
                citations.ToJson();
                citations.Property(citation => citation.DocumentType).HasConversion<string>();
            }
        );
        builder.ComplexProperty(finding => finding.Original, original => original.ToJson());

        builder
            .HasOne(finding => finding.ActionItem)
            .WithOne()
            .HasForeignKey<ActionItem>(item => new { item.TenantId, item.FindingId })
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(finding => new { finding.TenantId, finding.ReviewedBy })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActionItemConfiguration : IEntityTypeConfiguration<ActionItem>
{
    public void Configure(EntityTypeBuilder<ActionItem> builder)
    {
        builder.ToTable("action_items", table =>
            table.HasCheckConstraint(
                "ck_action_items_status",
                $"status IN ({SqlLiterals.List<ActionItemStatus>()})"
            )
        );
        builder.HasKey(item => new { item.TenantId, item.Id });
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Text).HasMaxLength(ActionItem.TextMaxLength);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.UpdatedBy })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InspectionCountConfiguration : IEntityTypeConfiguration<InspectionCount>
{
    public void Configure(EntityTypeBuilder<InspectionCount> builder)
    {
        builder.ToTable("inspection_counts");
        // One row per tenant and UTC day: the conflict target of the increment.
        builder.HasKey(count => new { count.TenantId, count.Day });
        builder
            .HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(count => count.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
