using System.Text.Encodings.Web;
using System.Text.Json;
using Normetry.Application.Inspections.ExportFeedback;

namespace Normetry.Api.Inspections;

/// <summary>
/// Development only: an inspection's feedback export as <c>{inspection id}.jsonl</c>, one JSON
/// object per line, under <see cref="DirectorySetting"/>. A new export replaces the inspection's
/// earlier file.
/// </summary>
public static class FeedbackExportFile
{
    /// <summary>Where the export writes; by default the repository's <c>evals/datasets/inspection-feedback</c>.</summary>
    public const string DirectorySetting = "InspectionFeedback:Directory";

    // People read the file before committing it and it never reaches a page, so accents and
    // symbols ("°F", "não") stay as they are instead of \u escapes.
    private static readonly JsonSerializerOptions FeedbackJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes the examples, creating the directory when needed, and returns the file's full path.</summary>
    public static async Task<string> WriteAsync(
        IConfiguration configuration,
        IHostEnvironment environment,
        Guid inspectionId,
        IReadOnlyList<FeedbackExample> examples,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetFullPath(
            configuration[DirectorySetting]
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
        var path = Path.Combine(directory, $"{inspectionId}.jsonl");
        await File.WriteAllTextAsync(path, JsonLines(examples), cancellationToken);
        return path;
    }

    private static string JsonLines(IEnumerable<FeedbackExample> examples) =>
        string.Concat(
            examples.Select(example => JsonSerializer.Serialize(example, FeedbackJson) + "\n")
        );
}
