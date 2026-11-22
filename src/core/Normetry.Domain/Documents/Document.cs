namespace Normetry.Domain.Documents;

/// <summary>
/// A document of one establishment (an SOP, a license, a log, a report) and its versions. Only one
/// version is in flight at a time; the current version, the one searches read, is the newest that
/// is indexed. A deleted document keeps its rows (soft delete).
/// </summary>
public sealed class Document
{
    public const int TitleMaxLength = 200;

    private readonly List<DocumentVersion> versions = [];

    private Document(
        Guid id,
        Guid tenantId,
        Guid establishmentId,
        string title,
        DocumentType type,
        DateTimeOffset createdAt
    )
    {
        Id = id;
        TenantId = tenantId;
        EstablishmentId = establishmentId;
        Title = title;
        Type = type;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid EstablishmentId { get; private set; }

    public string Title { get; private set; }

    public DocumentType Type { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>The last day the document is valid (a license, a certificate); none for most documents.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public IReadOnlyCollection<DocumentVersion> Versions => versions;

    /// <summary>The version uploaded last; its status is the one the document shows.</summary>
    public DocumentVersion Newest => versions.MaxBy(version => version.Number)!;

    /// <summary>The newest indexed version, or none yet. A failed new version leaves it current.</summary>
    public DocumentVersion? Current =>
        versions
            .Where(version => version.Status == DocumentVersionStatus.Indexed)
            .MaxBy(version => version.Number);

    /// <summary>The newest version waits for the AI service or is being ingested.</summary>
    public bool HasVersionInFlight => Newest.IsInFlight;

    /// <summary>When the document last changed: its newest status change.</summary>
    public DateTimeOffset UpdatedAt => versions.Max(version => version.StatusChangedAt);

    /// <summary>Its last valid day is before <paramref name="today"/>; never without a date.</summary>
    public bool IsExpiredOn(DateOnly today) => ExpiresOn < today;

    /// <summary>
    /// It expires from <paramref name="today"/> to <paramref name="days"/> days later, both
    /// included; never without a date.
    /// </summary>
    public bool ExpiresWithin(DateOnly today, int days) =>
        ExpiresOn >= today && ExpiresOn <= today.AddDays(days);

    /// <summary>A new document with its first version, uploaded by <paramref name="uploadedBy"/>.</summary>
    public static Document Create(
        Guid tenantId,
        Guid establishmentId,
        string title,
        DocumentType type,
        DocumentFile file,
        Guid uploadedBy,
        DateTimeOffset now
    )
    {
        var document = new Document(
            Guid.CreateVersion7(),
            Names.RequiredId(tenantId, nameof(tenantId)),
            Names.RequiredId(establishmentId, nameof(establishmentId)),
            Names.Required(title, TitleMaxLength, nameof(title)),
            Names.Defined(type, nameof(type)),
            now
        );
        document.versions.Add(DocumentVersion.Create(document, 1, file, uploadedBy, now));
        return document;
    }

    /// <summary>The next version. Refused while one is in flight or once the document is deleted.</summary>
    public DocumentVersion AddVersion(DocumentFile file, Guid uploadedBy, DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted document gets no new version.");
        }
        if (HasVersionInFlight)
        {
            throw new InvalidOperationException("Another version of the document is in flight.");
        }
        var version = DocumentVersion.Create(this, Newest.Number + 1, file, uploadedBy, now);
        versions.Add(version);
        return version;
    }

    /// <summary>Sets or clears the expiry date. A past date is allowed: the document is expired.</summary>
    public void SetExpiry(DateOnly? expiresOn)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted document keeps its expiry date.");
        }
        ExpiresOn = expiresOn;
    }

    /// <summary>Soft delete: the rows and blobs stay. Deleting again changes nothing.</summary>
    public void Delete(DateTimeOffset now) => DeletedAt ??= now;
}

// Values start at 1, so a missing field (0) is not a valid value.
public enum DocumentType
{
    /// <summary>Standard operating procedure.</summary>
    Sop = 1,
    License,

    /// <summary>Temperatures, cleaning, deliveries.</summary>
    Log,

    /// <summary>Lab or audit report.</summary>
    Report,
    Other,
}

public enum DocumentVersionStatus
{
    /// <summary>Stored and announced; waits for the AI service (the web app says "waiting").</summary>
    Uploaded = 1,
    Processing,
    Indexed,
    Failed,
}

/// <summary>What the core accepted of an uploaded file. The content type has no parameters.</summary>
public sealed record DocumentFile(
    string FileName,
    string Extension,
    string ContentType,
    long Size,
    string Sha256
);
