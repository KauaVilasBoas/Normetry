using Microsoft.EntityFrameworkCore;
using Normetry.Application.Documents;
using Normetry.Domain.Documents;

namespace Normetry.Infrastructure.Persistence;

// No tenant filter here: row-level security returns only the transaction's tenant rows.
internal sealed class DocumentRepository(CoreDbContext db) : IDocumentRepository
{
    public void Add(Document document) => db.Documents.Add(document);

    public async Task<Document?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT FROM core.documents WHERE id = {id} FOR UPDATE",
            cancellationToken
        );
        return await db
            .Documents.Include(document => document.Versions)
            .FirstOrDefaultAsync(document => document.Id == id, cancellationToken);
    }

    public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db
            .Documents.AsNoTracking()
            .Include(document => document.Versions)
            .FirstOrDefaultAsync(
                document => document.Id == id && document.DeletedAt == null,
                cancellationToken
            );

    public async Task<IReadOnlyList<Document>> ListAsync(
        Guid establishmentId,
        CancellationToken cancellationToken
    ) =>
        await db
            .Documents.AsNoTracking()
            .Include(document => document.Versions)
            .Where(document =>
                document.EstablishmentId == establishmentId && document.DeletedAt == null
            )
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        db.Documents.CountAsync(
            document => document.EstablishmentId == establishmentId && document.DeletedAt == null,
            cancellationToken
        );

    public async Task<IReadOnlyList<Document>> ListProcessingSinceForUpdateAsync(
        DateTimeOffset before,
        CancellationToken cancellationToken
    )
    {
        var ids = await db
            .Database.SqlQuery<Guid>(
                $"""
                SELECT d.id AS "Value" FROM core.documents d
                WHERE EXISTS (
                    SELECT FROM core.document_versions v
                    WHERE v.tenant_id = d.tenant_id AND v.document_id = d.id
                      AND v.status = 'Processing' AND v.status_changed_at < {before}
                )
                FOR UPDATE OF d SKIP LOCKED
                """
            )
            .ToListAsync(cancellationToken);
        return ids.Count == 0
            ? []
            : await db
                .Documents.Include(document => document.Versions)
                .Where(document => ids.Contains(document.Id))
                .ToListAsync(cancellationToken);
    }
}
