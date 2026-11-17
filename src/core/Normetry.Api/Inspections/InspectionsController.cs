using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Inspections;
using Normetry.Application.Inspections.ExportFeedback;
using Normetry.Application.Inspections.GetInspection;
using Normetry.Application.Inspections.ListInspections;
using Normetry.Application.Inspections.ReviewFinding;
using Normetry.Application.Inspections.SetActionItemStatus;
using Normetry.Application.Inspections.StartInspection;
using Normetry.Application.Usage;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Identity;
using Normetry.Domain.Inspections;

namespace Normetry.Api.Inspections;

/// <summary>
/// An establishment's simulated inspections. A start answers 202: the AI service runs it
/// afterwards, and its progress reaches the web app over <c>/hubs/inspections</c>. Another
/// tenant's ids answer 404 like unknown ones: RLS hides the rows.
/// </summary>
[ApiController]
[Route("establishments/{establishmentId:guid}/inspections")]
public sealed class InspectionsController(IDispatcher dispatcher, TimeProvider clock)
    : ControllerBase
{
    /// <summary>
    /// Starts one. 409 <c>inspection_running</c>, <c>no_indexed_documents</c> or
    /// <c>inspection_capacity</c>; 429
    /// <c>inspection_quota</c> with <c>Retry-After</c> to the next UTC midnight.
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Inspections.Run)]
    public async Task<IActionResult> Start(
        Guid establishmentId,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(
            new StartInspectionCommand(establishmentId),
            cancellationToken
        );
        if (result.IsSuccess)
        {
            return Accepted(
                $"/establishments/{establishmentId}/inspections/{result.Value.Id}",
                result.Value
            );
        }
        if (result.Error.Type == ErrorType.RateLimited)
        {
            var now = clock.GetUtcNow();
            Response.Headers.RetryAfter = (
                (long)Math.Ceiling((ChatQuotaResponse.NextReset(now) - now).TotalSeconds)
            ).ToString(CultureInfo.InvariantCulture);
        }
        return result.ToActionResult();
    }

    /// <summary>Newest first, with their findings counted by verdict.</summary>
    [HttpGet]
    [HasPermission(Permissions.Inspections.Read)]
    public async Task<IActionResult> List(Guid establishmentId, CancellationToken cancellationToken)
    {
        var result = await dispatcher.QueryAsync(
            new ListInspectionsQuery(establishmentId),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    /// <summary>One inspection with its per-theme progress, findings and action items.</summary>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Inspections.Read)]
    public async Task<IActionResult> Get(
        Guid establishmentId,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.QueryAsync(
            new GetInspectionQuery(establishmentId, id),
            cancellationToken
        );
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    // Every review answers 200 with the finding as GET returns it (its action included), 404
    // finding.not_found (action_item.not_found for an action) and 409 inspection_not_completed.

    [HttpPost("{id:guid}/findings/{findingId:guid}/accept")]
    [HasPermission(Permissions.Inspections.Review)]
    public Task<IActionResult> Accept(
        Guid establishmentId,
        Guid id,
        Guid findingId,
        CancellationToken cancellationToken
    ) =>
        ReviewAsync(
            new ReviewFindingCommand(establishmentId, id, findingId, ReviewDecision.Accept),
            cancellationToken
        );

    /// <summary><c>{ "note": "..." }</c>, required, at most 1000 characters.</summary>
    [HttpPost("{id:guid}/findings/{findingId:guid}/reject")]
    [HasPermission(Permissions.Inspections.Review)]
    public Task<IActionResult> Reject(
        Guid establishmentId,
        Guid id,
        Guid findingId,
        RejectRequest request,
        CancellationToken cancellationToken
    ) =>
        ReviewAsync(
            new ReviewFindingCommand(
                establishmentId,
                id,
                findingId,
                ReviewDecision.Reject,
                Note: request.Note
            ),
            cancellationToken
        );

    /// <summary>Any of <c>verdict</c>, <c>severity</c>, <c>rationale</c>, <c>actionText</c>, <c>dueOn</c>; a null or missing one stays.</summary>
    [HttpPatch("{id:guid}/findings/{findingId:guid}")]
    [HasPermission(Permissions.Inspections.Review)]
    public Task<IActionResult> Edit(
        Guid establishmentId,
        Guid id,
        Guid findingId,
        EditRequest request,
        CancellationToken cancellationToken
    ) =>
        ReviewAsync(
            new ReviewFindingCommand(
                establishmentId,
                id,
                findingId,
                ReviewDecision.Edit,
                Verdict: request.Verdict,
                Severity: request.Severity,
                Rationale: request.Rationale,
                ActionText: request.ActionText,
                DueOn: request.DueOn
            ),
            cancellationToken
        );

    [HttpPost("{id:guid}/action-items/{actionItemId:guid}/done")]
    [HasPermission(Permissions.Inspections.Review)]
    public Task<IActionResult> MarkDone(
        Guid establishmentId,
        Guid id,
        Guid actionItemId,
        CancellationToken cancellationToken
    ) =>
        ReviewAsync(
            new SetActionItemStatusCommand(
                establishmentId,
                id,
                actionItemId,
                ActionItemStatus.Done
            ),
            cancellationToken
        );

    [HttpPost("{id:guid}/action-items/{actionItemId:guid}/reopen")]
    [HasPermission(Permissions.Inspections.Review)]
    public Task<IActionResult> Reopen(
        Guid establishmentId,
        Guid id,
        Guid actionItemId,
        CancellationToken cancellationToken
    ) =>
        ReviewAsync(
            new SetActionItemStatusCommand(
                establishmentId,
                id,
                actionItemId,
                ActionItemStatus.Open
            ),
            cancellationToken
        );

    /// <summary>
    /// Development only (404 elsewhere): writes the inspection's rejected and edited findings as
    /// eval examples, one JSON object per line, to <c>{id}.jsonl</c> under
    /// <see cref="FeedbackDirectorySetting"/> (by default the repository's
    /// <c>evals/datasets/inspection-feedback</c>), replacing an earlier export. Answers
    /// <c>{ path, examples }</c>. The caller's tenant and RLS scope it like any request.
    /// </summary>
    [HttpPost("{id:guid}/feedback-export")]
    [HasPermission(Permissions.Inspections.Review)]
    public async Task<IActionResult> ExportFeedback(
        Guid establishmentId,
        Guid id,
        [FromServices] IHostEnvironment environment,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }
        var result = await dispatcher.QueryAsync(
            new ExportFeedbackQuery(establishmentId, id),
            cancellationToken
        );
        if (!result.IsSuccess)
        {
            return result.ToActionResult();
        }
        var directory = Path.GetFullPath(
            configuration[FeedbackDirectorySetting]
                ?? Path.Combine(
                    environment.ContentRootPath,
                    "..",
                    "..",
                    "..",
                    "evals",
                    "datasets",
                    "inspection-feedback"
                )
        );
        Directory.CreateDirectory(directory);
        // The id is a parsed Guid, so no client text reaches the path.
        var path = Path.Combine(directory, $"{id}.jsonl");
        await System.IO.File.WriteAllTextAsync(
            path,
            string.Concat(
                result.Value.Select(example =>
                    JsonSerializer.Serialize(example, FeedbackJson) + "\n"
                )
            ),
            cancellationToken
        );
        return Ok(new { path, examples = result.Value.Count });
    }

    /// <summary>Where the feedback export writes; by default the repository's <c>evals/datasets/inspection-feedback</c>.</summary>
    public const string FeedbackDirectorySetting = "InspectionFeedback:Directory";

    // People read the file before committing it and it never reaches a page, so accents and
    // symbols ("°F", "não") stay as they are instead of \u escapes.
    private static readonly JsonSerializerOptions FeedbackJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private async Task<IActionResult> ReviewAsync(
        ICommand<FindingResponse> command,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.SendAsync(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToActionResult();
    }

    public sealed record RejectRequest(string? Note);

    public sealed record EditRequest(
        string? Verdict,
        string? Severity,
        string? Rationale,
        string? ActionText,
        DateOnly? DueOn
    );
}
