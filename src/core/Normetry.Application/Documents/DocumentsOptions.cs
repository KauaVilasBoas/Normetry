namespace Normetry.Application.Documents;

/// <summary>The <c>Documents</c> settings, validated when the API starts.</summary>
public sealed class DocumentsOptions
{
    public const string Section = "Documents";

    /// <summary>Documents an establishment may hold, deleted ones not counted.</summary>
    public int MaxDocumentsPerEstablishment { get; init; } = 50;

    /// <summary>
    /// Versions a document may hold. Blobs are kept (retention is out of scope), so this and the
    /// document limit bound what an establishment stores.
    /// </summary>
    public int MaxVersionsPerDocument { get; init; } = 20;

    /// <summary>How long a version may stay Processing before it fails with <c>timed_out</c>.</summary>
    public TimeSpan ProcessingTimeout { get; init; } = TimeSpan.FromMinutes(15);

    public void Validate()
    {
        if (MaxDocumentsPerEstablishment < 1)
        {
            throw new InvalidOperationException(
                $"{Section}:{nameof(MaxDocumentsPerEstablishment)} must be at least 1."
            );
        }
        if (MaxVersionsPerDocument < 1)
        {
            throw new InvalidOperationException(
                $"{Section}:{nameof(MaxVersionsPerDocument)} must be at least 1."
            );
        }
        if (ProcessingTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{Section}:{nameof(ProcessingTimeout)} must be positive."
            );
        }
    }
}
