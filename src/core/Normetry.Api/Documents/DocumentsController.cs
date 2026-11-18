using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Normetry.Api.Auth;
using Normetry.Api.RateLimiting;
using Normetry.Application.Documents;
using Normetry.Application.Documents.DeleteDocument;
using Normetry.Application.Documents.GetDocument;
using Normetry.Application.Documents.ListDocuments;
using Normetry.Application.Documents.SetDocumentExpiry;
using Normetry.Application.Documents.UploadDocument;
using Normetry.Application.Documents.UploadDocumentVersion;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Documents;
using Normetry.Domain.Identity;

namespace Normetry.Api.Documents;

/// <summary>
/// An establishment's documents. Uploads are multipart and answer 202: the AI service ingests the
/// file afterwards, and its status changes reach the web app over SignalR. Another tenant's ids
/// answer 404 like unknown ones: RLS hides the rows.
/// </summary>
[ApiController]
public sealed class DocumentsController(IDispatcher dispatcher) : ControllerBase
{
    // The file limit plus room for the multipart framing and the other fields, so a file just
    // over 10 MiB still reaches the check and gets document.too_large.
    private const long UploadRequestLimit = DocumentFiles.MaxSize + 1024 * 1024;

    private static readonly Error InvalidExpiresOn = new(
        "validation",
        "One or more validation errors occurred.",
        ErrorType.Validation
    )
    {
        FieldErrors = new Dictionary<string, string[]>
        {
            ["ExpiresOn"] = ["Give the expiry date as YYYY-MM-DD."],
        },
    };

    /// <summary>
    /// A new document with its first version (form fields <c>file</c>, <c>title</c>, <c>type</c>
    /// and an optional <c>expiresOn</c>, <c>YYYY-MM-DD</c>).
    /// </summary>
    [HttpPost("establishments/{establishmentId:guid}/documents")]
    [HasPermission(Permissions.Documents.Upload)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<IActionResult> Upload(
        Guid establishmentId,
        [FromForm] UploadDocumentForm form,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDate(form.ExpiresOn, out var expiresOn))
        {
            return Result.Failure(InvalidExpiresOn).ToActionResult();
        }
        var result = await dispatcher.SendAsync(
            new UploadDocumentCommand(
                establishmentId,
                form.Title ?? "",
                TypeByName(form.Type),
                await ReadAsync(form.File, cancellationToken),
                expiresOn
            ),
            cancellationToken
        );
        return result.IsSuccess
            ? Accepted($"/documents/{result.Value.Id}", result.Value)
            : result.ToActionResult();
    }

    /// <summary>The next version of a document (form field <c>file</c>). 409 while one is in flight.</summary>
    [HttpPost("documents/{documentId:guid}/versions")]
    [HasPermission(Permissions.Documents.Upload)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<IActionResult> UploadVersion(
        Guid documentId,
        [FromForm] UploadDocumentVersionForm form,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(
            new UploadDocumentVersionCommand(
                documentId,
                await ReadAsync(form.File, cancellationToken)
            ),
            cancellationToken
        );
        return result.IsSuccess
            ? Accepted($"/documents/{result.Value.Id}", result.Value)
            : result.ToActionResult();
    }

    /// <summary>
    /// The establishment's documents; <c>expired=true</c> keeps those past their expiry date,
    /// <c>expiringWithinDays=N</c> (1 to 365) those expiring from today to today plus N days (UTC).
    /// </summary>
    [HttpGet("establishments/{establishmentId:guid}/documents")]
    [HasPermission(Permissions.Documents.Read)]
    public async Task<IActionResult> List(
        Guid establishmentId,
        CancellationToken cancellationToken,
        bool expired = false,
        int? expiringWithinDays = null
    )
    {
        var result = await dispatcher.QueryAsync(
            new ListDocumentsQuery(establishmentId, expired, expiringWithinDays),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    /// <summary>Sets or, with <c>null</c>, clears the document's expiry date. 204.</summary>
    [HttpPatch("establishments/{establishmentId:guid}/documents/{documentId:guid}")]
    [HasPermission(Permissions.Documents.Upload)]
    public async Task<IActionResult> SetExpiry(
        Guid establishmentId,
        Guid documentId,
        SetDocumentExpiryRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(
            new SetDocumentExpiryCommand(establishmentId, documentId, request.ExpiresOn),
            cancellationToken
        );
        return result.IsSuccess ? NoContent() : result.ToActionResult();
    }

    [HttpGet("documents/{documentId:guid}")]
    [HasPermission(Permissions.Documents.Read)]
    public async Task<IActionResult> Get(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(
            new GetDocumentQuery(documentId),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    /// <summary>Soft delete: 204, then the AI service removes the document's chunks.</summary>
    [HttpDelete("documents/{documentId:guid}")]
    [HasPermission(Permissions.Documents.Delete)]
    public async Task<IActionResult> Delete(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(
            new DeleteDocumentCommand(documentId),
            cancellationToken
        );
        return result.IsSuccess ? NoContent() : result.ToActionResult();
    }

    // Enums travel by name, as in JSON; a number or an unknown name is 0, which validation refuses.
    private static DocumentType TypeByName(string? name) =>
        Enum.GetNames<DocumentType>().Contains(name, StringComparer.Ordinal)
            ? Enum.Parse<DocumentType>(name!)
            : default;

    // Strict ISO dates only, whatever the server's culture; an empty field means no date.
    private static bool TryParseDate(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        if (
            !DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed
            )
        )
        {
            return false;
        }
        date = parsed;
        return true;
    }

    private static async Task<UploadedFile?> ReadAsync(
        IFormFile? file,
        CancellationToken cancellationToken
    )
    {
        if (file is null)
        {
            return null;
        }
        using var buffer = new MemoryStream((int)Math.Min(file.Length, UploadRequestLimit));
        await file.CopyToAsync(buffer, cancellationToken);
        return new UploadedFile(file.FileName, file.ContentType, buffer.ToArray());
    }
}

public sealed class UploadDocumentForm
{
    public IFormFile? File { get; init; }

    public string? Title { get; init; }

    /// <summary>A <see cref="DocumentType"/> name: <c>Sop</c>, <c>License</c>, <c>Log</c>, <c>Report</c> or <c>Other</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Optional expiry date, <c>YYYY-MM-DD</c>.</summary>
    public string? ExpiresOn { get; init; }
}

public sealed class UploadDocumentVersionForm
{
    public IFormFile? File { get; init; }
}

/// <summary><c>{ "expiresOn": "YYYY-MM-DD" | null }</c>; the field is required, so <c>{}</c> clears nothing.</summary>
public sealed class SetDocumentExpiryRequest
{
    public required DateOnly? ExpiresOn { get; init; }
}
