using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Documents;
using Normetry.Application.Documents.DeleteDocument;
using Normetry.Application.Documents.GetDocument;
using Normetry.Application.Documents.ListDocuments;
using Normetry.Application.Documents.UploadDocument;
using Normetry.Application.Documents.UploadDocumentVersion;
using Normetry.BuildingBlocks.Cqrs;
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

    /// <summary>A new document with its first version (form fields <c>file</c>, <c>title</c>, <c>type</c>).</summary>
    [HttpPost("establishments/{establishmentId:guid}/documents")]
    [HasPermission(Permissions.Documents.Upload)]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public async Task<IActionResult> Upload(
        Guid establishmentId,
        [FromForm] UploadDocumentForm form,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(
            new UploadDocumentCommand(
                establishmentId,
                form.Title ?? "",
                TypeByName(form.Type),
                await ReadAsync(form.File, cancellationToken)
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

    [HttpGet("establishments/{establishmentId:guid}/documents")]
    [HasPermission(Permissions.Documents.Read)]
    public async Task<IActionResult> List(Guid establishmentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(
            new ListDocumentsQuery(establishmentId),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
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
}

public sealed class UploadDocumentVersionForm
{
    public IFormFile? File { get; init; }
}
