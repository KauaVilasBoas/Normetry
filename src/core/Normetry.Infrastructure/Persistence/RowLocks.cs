using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Row locks (<c>SELECT ... FOR UPDATE</c>), held until the transaction ends. The table and column
/// names come from the EF model and every value is a parameter. The statement runs in the
/// context's transaction, which the tenant interceptor scoped to the tenant, so RLS decides which
/// row it can lock: another tenant's row is never locked, only not found.
/// </summary>
internal static class RowLocks
{
    // The property a locked entity is found by, and the one naming the establishment of the rows
    // a route reaches under an establishment (documents, inspections).
    private const string IdProperty = "Id";
    private const string EstablishmentIdProperty = "EstablishmentId";

    /// <summary>Locks the <typeparamref name="TEntity"/> row with this id. Returns whether there is one.</summary>
    public static Task<bool> LockByIdAsync<TEntity>(
        this CoreDbContext db,
        Guid id,
        CancellationToken cancellationToken
    )
        where TEntity : class => db.LockWhereAsync<TEntity>([(IdProperty, id)], cancellationToken);

    /// <summary>
    /// Locks the <typeparamref name="TEntity"/> row with this id when it is the establishment's: a
    /// condition next to RLS, never instead of it, so another establishment's row stays unlocked.
    /// Returns whether there is one.
    /// </summary>
    public static Task<bool> LockOfEstablishmentAsync<TEntity>(
        this CoreDbContext db,
        Guid establishmentId,
        Guid id,
        CancellationToken cancellationToken
    )
        where TEntity : class =>
        db.LockWhereAsync<TEntity>(
            [(IdProperty, id), (EstablishmentIdProperty, establishmentId)],
            cancellationToken
        );

    private static async Task<bool> LockWhereAsync<TEntity>(
        this CoreDbContext db,
        (string Property, Guid Value)[] equalities,
        CancellationToken cancellationToken
    )
        where TEntity : class
    {
        var statement = LockStatement(
            db,
            db.Set<TEntity>().EntityType,
            equalities.Select(equality => equality.Property)
        );
        var locked = await db
            .Database.SqlQueryRaw<Guid>(
                statement,
                [.. equalities.Select(equality => (object)equality.Value)]
            )
            .ToListAsync(cancellationToken);
        return locked.Count > 0;
    }

    /// <summary>
    /// <c>SELECT id AS "Value" FROM core.table WHERE column = {0} AND ... FOR UPDATE</c>, one
    /// parameter placeholder per property, in order.
    /// </summary>
    private static string LockStatement(
        CoreDbContext db,
        IEntityType entity,
        IEnumerable<string> properties
    )
    {
        var sql = db.GetService<ISqlGenerationHelper>();
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string Column(string property) =>
            sql.DelimitIdentifier(entity.GetProperty(property).GetColumnName(table)!);
        var tableName = sql.DelimitIdentifier(table.Name, table.Schema);
        var conditions = properties.Select(
            (property, index) => $"{Column(property)} = {{{index}}}"
        );
        return $"""
            SELECT {Column(IdProperty)} AS "Value" FROM {tableName}
            WHERE {string.Join(" AND ", conditions)}
            FOR UPDATE
            """;
    }
}
