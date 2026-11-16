using System.IO.Compression;
using System.Security.Cryptography;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents;

/// <summary>A file as the client sent it. The content is read in full (at most a little over 10 MiB).</summary>
public sealed record UploadedFile(string FileName, string? ContentType, byte[] Content);

/// <summary>
/// Which uploads the core accepts: PDF, DOCX, TXT and CSV of up to 10 MiB, recognized by their
/// extension, their content type and their first bytes. The page count needs a parser, so the AI
/// service checks it.
/// </summary>
public static class DocumentFiles
{
    /// <summary>10 MiB, the AI service's <c>too_large</c> limit too.</summary>
    public const long MaxSize = 10 * 1024 * 1024;

    private static readonly Format[] Formats =
    [
        new(".pdf", "application/pdf", ["application/pdf"], IsPdf),
        new(
            ".docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
            IsDocx
        ),
        new(".txt", "text/plain", ["text/plain"], IsText),
        // Browsers on Windows with Excel installed send .csv as application/vnd.ms-excel.
        new(
            ".csv",
            "text/csv",
            ["text/csv", "application/csv", "application/vnd.ms-excel", "text/plain"],
            IsText
        ),
    ];

    /// <summary>The canonical content type of an accepted extension, or null.</summary>
    public static string? ContentTypeOf(string fileName) =>
        Formats
            .FirstOrDefault(format =>
                format.Extension == Path.GetExtension(fileName).ToLowerInvariant()
            )
            ?.ContentType;

    /// <summary>The accepted file, with its canonical content type, or why it was refused.</summary>
    public static Result<DocumentFile> Inspect(UploadedFile file)
    {
        var name = DisplayName(file.FileName);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var format = Formats.FirstOrDefault(format => format.Extension == extension);
        var contentType = file.ContentType?.Split(';')[0].Trim().ToLowerInvariant();
        if (format is null || !format.ContentTypes.Contains(contentType))
        {
            return DocumentErrors.UnsupportedFormat;
        }
        if (file.Content.Length == 0)
        {
            return DocumentErrors.Empty;
        }
        if (file.Content.Length > MaxSize)
        {
            return DocumentErrors.TooLarge;
        }
        if (!format.Matches(file.Content))
        {
            return DocumentErrors.InvalidContent;
        }
        return new DocumentFile(
            name,
            format.Extension,
            format.ContentType,
            file.Content.Length,
            Convert.ToHexStringLower(SHA256.HashData(file.Content))
        );
    }

    /// <summary>
    /// The name without any directory part or control character, at most 255 characters with its
    /// extension kept (an extension that long is not: no format has one). Never cuts a surrogate
    /// pair in half.
    /// </summary>
    private static string DisplayName(string fileName)
    {
        var name = new string(
            Path.GetFileName(fileName.Replace('\\', '/')).Where(c => !char.IsControl(c)).ToArray()
        ).Trim();
        if (name.Length <= DocumentVersion.FileNameMaxLength)
        {
            return name;
        }
        var extension = Path.GetExtension(name);
        if (extension.Length >= DocumentVersion.FileNameMaxLength)
        {
            extension = "";
        }
        var length = DocumentVersion.FileNameMaxLength - extension.Length;
        if (char.IsHighSurrogate(name[length - 1]))
        {
            length--;
        }
        return name[..length] + extension;
    }

    private static bool IsPdf(byte[] content) => content.AsSpan().StartsWith("%PDF-"u8);

    /// <summary>A zip that holds the main part of a Word document (reads only the zip directory).</summary>
    private static bool IsDocx(byte[] content)
    {
        if (!content.AsSpan().StartsWith("PK\u0003\u0004"u8))
        {
            return false;
        }
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            return zip.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Text has no NUL byte and is not a PDF or a zip under another name.</summary>
    private static bool IsText(byte[] content) =>
        !content.AsSpan().Contains((byte)0)
        && !content.AsSpan().StartsWith("%PDF-"u8)
        && !content.AsSpan().StartsWith("PK\u0003\u0004"u8);

    private sealed record Format(
        string Extension,
        string ContentType,
        string[] ContentTypes,
        Func<byte[], bool> Matches
    );
}
