using Normetry.Application.Abstractions;

namespace Normetry.Application.Documents;

// The payloads of the document messages (src/shared/contracts/messages, version 1). The envelope
// carries the message id, the tenant and the time; the payload never carries a tenant.

/// <summary>core to ai: a version was stored in blob storage and waits for ingestion.</summary>
public sealed record DocumentUploaded(
    Guid EstablishmentId,
    Guid DocumentId,
    Guid VersionId,
    int VersionNumber,
    string BlobPath,
    string ContentType,
    long Size,
    string Sha256
) : IIntegrationMessage
{
    public static string Type => "DocumentUploaded";

    public static int Version => 1;

    public static string RoutingKey => "document.uploaded.v1";
}

/// <summary>core to ai: a document was soft-deleted.</summary>
public sealed record DocumentDeleted(Guid EstablishmentId, Guid DocumentId) : IIntegrationMessage
{
    public static string Type => "DocumentDeleted";

    public static int Version => 1;

    public static string RoutingKey => "document.deleted.v1";
}

/// <summary>An ai to core event about one document version.</summary>
public interface IDocumentVersionEvent
{
    Guid EstablishmentId { get; }

    Guid DocumentId { get; }

    Guid VersionId { get; }

    int VersionNumber { get; }
}

/// <summary>ai to core: ingestion of a version started.</summary>
public sealed record DocumentIngestionStarted(
    Guid EstablishmentId,
    Guid DocumentId,
    Guid VersionId,
    int VersionNumber
) : IIntegrationMessage, IDocumentVersionEvent
{
    public static string Type => "DocumentIngestionStarted";

    public static int Version => 1;

    public static string RoutingKey => "document.ingestion-started.v1";
}

/// <summary>ai to core: a version's chunks are stored and searchable.</summary>
public sealed record DocumentIndexed(
    Guid EstablishmentId,
    Guid DocumentId,
    Guid VersionId,
    int VersionNumber,
    int ChunkCount,
    int? PageCount,
    string Language
) : IIntegrationMessage, IDocumentVersionEvent
{
    public static string Type => "DocumentIndexed";

    public static int Version => 1;

    public static string RoutingKey => "document.indexed.v1";

    public static readonly IReadOnlySet<string> Languages = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "en",
        "pt",
    };
}

/// <summary>ai to core: a version cannot be ingested.</summary>
public sealed record DocumentIngestionFailed(
    Guid EstablishmentId,
    Guid DocumentId,
    Guid VersionId,
    int VersionNumber,
    string ReasonCode,
    string ReasonText
) : IIntegrationMessage, IDocumentVersionEvent
{
    public const int ReasonTextMaxLength = 500;

    public static string Type => "DocumentIngestionFailed";

    public static int Version => 1;

    public static string RoutingKey => "document.ingestion-failed.v1";

    /// <summary>The contract's codes; a new one is a new message version.</summary>
    public static readonly IReadOnlySet<string> ReasonCodes = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "unsupported_format",
        "no_text_layer",
        "too_many_pages",
        "too_large",
        "blob_missing",
        "blob_path_outside_tenant",
        "parse_error",
        "internal",
    };
}
