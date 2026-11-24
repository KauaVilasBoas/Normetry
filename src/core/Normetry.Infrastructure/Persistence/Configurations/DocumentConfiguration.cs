using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Normetry.Domain.Documents;
using Normetry.Domain.Establishments;
using Normetry.Domain.Identity;

namespace Normetry.Infrastructure.Persistence.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", table =>
            table.HasCheckConstraint(
                "ck_documents_type",
                $"type IN ({SqlLiterals.List<DocumentType>()})"
            )
        );
        // Same shape as profiles and users: (tenant_id, id), referenced by the versions.
        builder.HasKey(document => new { document.TenantId, document.Id });
        builder.Property(document => document.Id).ValueGeneratedNever();
        builder.Property(document => document.Title).HasMaxLength(Document.TitleMaxLength);
        builder.Property(document => document.Type).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(document => document.Newest);
        builder.Ignore(document => document.Current);

        // The composite key keeps a document of one tenant from pointing at another tenant's
        // establishment even by id: foreign key checks do not go through RLS.
        builder
            .HasOne<Establishment>()
            .WithMany()
            .HasForeignKey(document => new { document.TenantId, document.EstablishmentId })
            .HasPrincipalKey(establishment => new { establishment.TenantId, establishment.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasMany(document => document.Versions)
            .WithOne()
            .HasForeignKey(version => new { version.TenantId, version.DocumentId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions", table =>
            table.HasCheckConstraint(
                "ck_document_versions_status",
                $"status IN ({SqlLiterals.List<DocumentVersionStatus>()})"
            )
        );
        builder.HasKey(version => new { version.TenantId, version.Id });
        builder.Property(version => version.Id).ValueGeneratedNever();
        builder
            .Property(version => version.FileName)
            .HasMaxLength(DocumentVersion.FileNameMaxLength);
        builder
            .Property(version => version.ContentType)
            .HasMaxLength(DocumentVersion.ContentTypeMaxLength);
        builder.Property(version => version.Sha256).HasMaxLength(64);
        builder
            .Property(version => version.BlobPath)
            .HasMaxLength(DocumentVersion.BlobPathMaxLength);
        builder.Property(version => version.Status).HasConversion<string>().HasMaxLength(20);
        builder
            .Property(version => version.FailureCode)
            .HasMaxLength(DocumentVersion.FailureCodeMaxLength);
        builder
            .Property(version => version.FailureReason)
            .HasMaxLength(DocumentVersion.FailureReasonMaxLength);
        builder.Property(version => version.Language).HasMaxLength(10);

        // One number per document; also the version list's order.
        builder
            .HasIndex(version => new
            {
                version.TenantId,
                version.DocumentId,
                version.Number,
            })
            .IsUnique();
        // The timeout sweep: versions Processing since before a cutoff.
        builder
            .HasIndex(version => new { version.TenantId, version.StatusChangedAt })
            .HasFilter($"status = {SqlLiterals.Quote(nameof(DocumentVersionStatus.Processing))}");
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(version => new { version.TenantId, version.UploadedBy })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
