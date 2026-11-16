namespace Normetry.Domain.Documents;

/// <summary>
/// One uploaded file of a <see cref="Document"/>. Its status runs Uploaded, Processing, then
/// Indexed or Failed, driven by the AI service's events; every transition method returns whether
/// it changed anything, so a late or repeated event is a no-op.
/// </summary>
public sealed class DocumentVersion
{
    public const int FileNameMaxLength = 255;
    public const int FailureCodeMaxLength = 50;
    public const int FailureReasonMaxLength = 500;

    /// <summary>The core's own failure code: Processing for longer than the timeout.</summary>
    public const string TimedOut = "timed_out";

    private DocumentVersion(
        Guid id,
        Guid tenantId,
        Guid documentId,
        int number,
        string fileName,
        string contentType,
        long size,
        string sha256,
        string blobPath,
        Guid uploadedBy,
        DateTimeOffset uploadedAt
    )
    {
        Id = id;
        TenantId = tenantId;
        DocumentId = documentId;
        Number = number;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        Sha256 = sha256;
        BlobPath = blobPath;
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
        Status = DocumentVersionStatus.Uploaded;
        StatusChangedAt = uploadedAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid DocumentId { get; private set; }

    /// <summary>1 for the first upload, then 2, 3, ...</summary>
    public int Number { get; private set; }

    /// <summary>The name the file was uploaded with, for display only.</summary>
    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long Size { get; private set; }

    /// <summary>Lower-case hex.</summary>
    public string Sha256 { get; private set; }

    /// <summary>
    /// <c>tenants/{tenant}/establishments/{establishment}/documents/{document}/versions/{version}/file{ext}</c>,
    /// ids lower case. The last segment is fixed, so nothing the client sent reaches the path, logs
    /// or traces.
    /// </summary>
    public string BlobPath { get; private set; }

    /// <summary>The uploader's <c>core.users</c> id.</summary>
    public Guid UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public DocumentVersionStatus Status { get; private set; }

    public DateTimeOffset StatusChangedAt { get; private set; }

    /// <summary>A code of the contract's failure reasons, or <see cref="TimedOut"/>.</summary>
    public string? FailureCode { get; private set; }

    public string? FailureReason { get; private set; }

    public int? ChunkCount { get; private set; }

    /// <summary>Pages of a PDF; null for DOCX, TXT and CSV.</summary>
    public int? PageCount { get; private set; }

    public string? Language { get; private set; }

    internal static DocumentVersion Create(
        Document document,
        int number,
        DocumentFile file,
        Guid uploadedBy,
        DateTimeOffset now
    )
    {
        var id = Guid.CreateVersion7();
        var blobPath =
            $"tenants/{document.TenantId}/establishments/{document.EstablishmentId}"
            + $"/documents/{document.Id}/versions/{id}/file{file.Extension}";
        return new(
            id,
            document.TenantId,
            document.Id,
            number,
            Names.Required(file.FileName, FileNameMaxLength, nameof(file)),
            file.ContentType,
            file.Size,
            file.Sha256,
            blobPath,
            Names.RequiredId(uploadedBy, nameof(uploadedBy)),
            now
        );
    }

    /// <summary>The AI service started: Uploaded becomes Processing.</summary>
    public bool StartProcessing(DateTimeOffset now) =>
        Status == DocumentVersionStatus.Uploaded && Move(DocumentVersionStatus.Processing, now);

    /// <summary>
    /// The version's chunks are live: Indexed from any other status, also after a timeout or a
    /// failure, because the status must say what search reads.
    /// </summary>
    public bool MarkIndexed(int chunkCount, int? pageCount, string language, DateTimeOffset now)
    {
        if (Status == DocumentVersionStatus.Indexed)
        {
            return false;
        }
        ChunkCount = chunkCount;
        PageCount = pageCount;
        Language = language;
        FailureCode = null;
        FailureReason = null;
        return Move(DocumentVersionStatus.Indexed, now);
    }

    /// <summary>The AI service gave up: Failed, from Uploaded or Processing only.</summary>
    public bool MarkFailed(string code, string reason, DateTimeOffset now)
    {
        if (Status is not (DocumentVersionStatus.Uploaded or DocumentVersionStatus.Processing))
        {
            return false;
        }
        FailureCode = Truncate(code, FailureCodeMaxLength);
        FailureReason = Truncate(reason, FailureReasonMaxLength);
        return Move(DocumentVersionStatus.Failed, now);
    }

    /// <summary>
    /// Processing for at least <paramref name="timeout"/>: Failed with <see cref="TimedOut"/>.
    /// Uploaded never times out: the queue is durable and the version waits for the AI service.
    /// </summary>
    public bool TimeOut(TimeSpan timeout, DateTimeOffset now) =>
        Status == DocumentVersionStatus.Processing
        && now - StatusChangedAt >= timeout
        && MarkFailed(TimedOut, "Processing took too long and was stopped.", now);

    private bool Move(DocumentVersionStatus status, DateTimeOffset now)
    {
        Status = status;
        StatusChangedAt = now;
        return true;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
