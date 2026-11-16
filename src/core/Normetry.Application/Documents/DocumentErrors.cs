using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Documents;

/// <summary>The stable codes of the document refusals.</summary>
public static class DocumentErrors
{
    // Another tenant's document is invisible under row-level security, so it is not found either.
    public static readonly Error NotFound = new(
        "document.not_found",
        "The document was not found.",
        ErrorType.NotFound
    );

    public static readonly Error UnsupportedFormat = new(
        "document.unsupported_format",
        "Upload a PDF, DOCX, TXT or CSV file: the extension and the content type must match one of them.",
        ErrorType.Validation
    );

    public static readonly Error InvalidContent = new(
        "document.invalid_content",
        "The file's content is not a valid PDF, DOCX, TXT or CSV file.",
        ErrorType.Validation
    );

    public static readonly Error Empty = new(
        "document.empty",
        "The file is empty.",
        ErrorType.Validation
    );

    public static readonly Error TooLarge = new(
        "document.too_large",
        $"The file is larger than {DocumentFiles.MaxSize} bytes (10 MiB).",
        ErrorType.Validation
    );

    public static readonly Error VersionInFlight = new(
        "document.version_in_flight",
        "Another version of this document is still waiting or processing. Try again when it finishes.",
        ErrorType.Conflict
    );

    public static Error LimitReached(int limit) =>
        new(
            "document.limit_reached",
            $"The establishment already has {limit} documents. Delete one before uploading another.",
            ErrorType.Conflict
        );
}
