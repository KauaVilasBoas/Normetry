using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Inspections;
using Normetry.Domain.Documents;
using Normetry.Domain.Establishments;
using Normetry.Domain.Inspections;

namespace Normetry.Infrastructure.Messaging;

/// <summary>The body of every queue message: <c>envelope.v1.schema.json</c>.</summary>
internal sealed record Envelope<TPayload>(
    Guid MessageId,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    TPayload Payload
);

/// <summary>A message read from the queue. <see cref="TenantId"/> comes from the body only.</summary>
internal sealed record InboundMessage(
    Guid MessageId,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    object Payload
);

/// <summary>A body off the contract: rejected without requeue, which dead-letters it at once.</summary>
internal sealed class MessageFormatException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// The JSON of <c>src/shared/contracts/messages</c>: UTF-8, camelCase, lower-case 8-4-4-4-12 ids
/// and RFC 3339 times with an offset. Writers produce exactly their schema; the reader ignores
/// payload fields it does not know and refuses anything else off the contract.
/// </summary>
internal static partial class MessageJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // camelCase only, as the contract says: "TenantId" is not the envelope's tenant.
        PropertyNameCaseInsensitive = false,
        // A missing field or a null where the contract has none fails the read.
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        // Domain enums by name, as the contract writes them (US, Restaurant, Sop); never numbers.
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string Serialize<TMessage>(
        Guid messageId,
        Guid tenantId,
        DateTimeOffset occurredAt,
        TMessage payload
    )
        where TMessage : IIntegrationMessage =>
        JsonSerializer.Serialize(
            new Envelope<TMessage>(
                messageId,
                TMessage.Type,
                TMessage.Version,
                occurredAt.ToUniversalTime(),
                tenantId,
                payload
            ),
            Options
        );

    /// <summary>Reads any message of the contract. Throws <see cref="MessageFormatException"/>.</summary>
    public static InboundMessage Parse(ReadOnlySpan<byte> body)
    {
        Envelope<JsonElement> envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope<JsonElement>>(body, Options)
                ?? throw new MessageFormatException("The body is null.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new MessageFormatException("The body is not a message envelope.", error);
        }

        Require(envelope.TenantId != Guid.Empty, "tenantId must not be the nil UUID");
        Require(envelope.Payload.ValueKind == JsonValueKind.Object, "payload must be an object");
        object payload = (envelope.Type, envelope.Version) switch
        {
            ("DocumentUploaded", 1) => Check(Read<DocumentUploaded>(envelope.Payload)),
            ("DocumentDeleted", 1) => Read<DocumentDeleted>(envelope.Payload),
            ("DocumentIngestionStarted", 1) => Check(
                Read<DocumentIngestionStarted>(envelope.Payload)
            ),
            ("DocumentIndexed", 1) => Check(Read<DocumentIndexed>(envelope.Payload)),
            ("DocumentIngestionFailed", 1) => Check(
                Read<DocumentIngestionFailed>(envelope.Payload)
            ),
            ("InspectionRequested", 1) => Check(Read<InspectionRequested>(envelope.Payload)),
            ("InspectionStarted", 1) => Check(Read<InspectionStarted>(envelope.Payload)),
            ("InspectionProgressed", 1) => Check(Read<InspectionProgressed>(envelope.Payload)),
            ("InspectionCompleted", 1) => Check(Read<InspectionCompleted>(envelope.Payload)),
            ("InspectionFailed", 1) => Check(Read<InspectionFailed>(envelope.Payload)),
            _ => throw new MessageFormatException(
                $"Unknown message {envelope.Type} v{envelope.Version}."
            ),
        };
        return new InboundMessage(
            envelope.MessageId,
            envelope.Type,
            envelope.Version,
            envelope.OccurredAt,
            envelope.TenantId,
            payload
        );
    }

    private static T Read<T>(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<T>(Options)
                ?? throw new MessageFormatException("The payload is null.");
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new MessageFormatException($"The payload is not a {typeof(T).Name}.", error);
        }
    }

    private static DocumentUploaded Check(DocumentUploaded message)
    {
        RequireVersionNumber(message.VersionNumber);
        Require(message.BlobPath.Length is >= 1 and <= 1024, "blobPath must be 1 to 1024 long");
        Require(message.ContentType.Length is >= 1 and <= 255, "contentType must be 1 to 255 long");
        Require(message.Size >= 0, "size must not be negative");
        Require(Sha256Pattern().IsMatch(message.Sha256), "sha256 must be lower-case hex");
        if (message.Title is not null)
        {
            RequireLength(message.Title, 1, 200, "title");
        }
        RequireDefined(message.DocumentType, "documentType");
        return message;
    }

    private static DocumentIngestionStarted Check(DocumentIngestionStarted message)
    {
        RequireVersionNumber(message.VersionNumber);
        return message;
    }

    private static DocumentIndexed Check(DocumentIndexed message)
    {
        RequireVersionNumber(message.VersionNumber);
        Require(message.ChunkCount >= 1, "chunkCount must be at least 1");
        Require(message.PageCount is null or >= 1, "pageCount must be null or at least 1");
        Require(DocumentIndexed.Languages.Contains(message.Language), "unknown language");
        return message;
    }

    private static DocumentIngestionFailed Check(DocumentIngestionFailed message)
    {
        RequireVersionNumber(message.VersionNumber);
        Require(
            DocumentIngestionFailureReasons.ContractCodes.Contains(message.ReasonCode),
            "unknown reasonCode"
        );
        Require(
            message.ReasonText.Length is >= 1 and <= DocumentIngestionFailed.ReasonTextMaxLength,
            "reasonText must be 1 to 500 long"
        );
        return message;
    }

    private static InspectionRequested Check(InspectionRequested message)
    {
        RequireDefined<Jurisdiction>(message.Jurisdiction, "jurisdiction");
        RequireDefined<EstablishmentType>(message.EstablishmentType, "establishmentType");
        Require(message.TokenCeiling >= 1, "tokenCeiling must be at least 1");
        RequireLength(
            message.RequestedBy,
            1,
            InspectionRequested.RequestedByMaxLength,
            "requestedBy"
        );
        return message;
    }

    private static InspectionStarted Check(InspectionStarted message)
    {
        Require(message.ItemsTotal >= 0, "itemsTotal must not be negative");
        RequireThemes(message.Themes);
        return message;
    }

    private static InspectionProgressed Check(InspectionProgressed message)
    {
        Require(message.ItemsEvaluated >= 0, "itemsEvaluated must not be negative");
        Require(message.ItemsTotal >= 0, "itemsTotal must not be negative");
        RequireThemes(message.Themes);
        return message;
    }

    private static InspectionCompleted Check(InspectionCompleted message)
    {
        Require(message.InputTokens >= 0, "inputTokens must not be negative");
        Require(message.OutputTokens >= 0, "outputTokens must not be negative");
        Require(message.CostUsd >= 0, "costUsd must not be negative");
        foreach (var finding in message.Findings)
        {
            // Nullable annotations do not reach list elements, so a null one is checked here.
            Require(finding is not null, "a finding must not be null");
            RequireLength(finding.ItemId, 1, 100, "itemId");
            Require(ThemeProgress.Themes.Contains(finding.Theme), "unknown theme");
            RequireLength(finding.Title, 1, 200, "title");
            Require(Severities.All.Contains(finding.Severity), "unknown severity");
            Require(Verdicts.All.Contains(finding.Verdict), "unknown verdict");
            Require(Confidences.All.Contains(finding.Confidence), "unknown confidence");
            RequireLength(finding.Rationale, 1, 4000, "rationale");
            Require(finding.RegulationCitations.Count >= 1, "a finding cites the regulation");
            foreach (var citation in finding.RegulationCitations)
            {
                Require(citation is not null, "a regulation citation must not be null");
                RequireLength(citation.SourceTitle, 1, 500, "sourceTitle");
                RequireLength(citation.SourceVersion, 1, 100, "sourceVersion");
                RequireLength(citation.SectionPath, 1, 1000, "sectionPath");
                RequireQuote(citation.Language, citation.Text, citation.Quote);
                RequireOffsets(citation.QuoteStart, citation.QuoteEnd);
            }
            foreach (var citation in finding.EvidenceCitations)
            {
                Require(citation is not null, "an evidence citation must not be null");
                if (citation.DocumentTitle is not null)
                {
                    RequireLength(citation.DocumentTitle, 1, 200, "documentTitle");
                }
                RequireDefined(citation.DocumentType, "documentType");
                RequireVersionNumber(citation.VersionNumber);
                RequireLength(citation.SectionPath, 0, 1000, "sectionPath");
                RequireQuote(citation.Language, citation.Text, citation.Quote);
                RequireOffsets(citation.QuoteStart, citation.QuoteEnd);
            }
            if (finding.Action is { } action)
            {
                RequireLength(action.Text, 1, 2000, "action.text");
                Require(action.DueInDays >= 1, "action.dueInDays must be at least 1");
            }
        }
        return message;
    }

    private static InspectionFailed Check(InspectionFailed message)
    {
        Require(
            InspectionFailureReasons.ContractCodes.Contains(message.ReasonCode),
            "unknown reasonCode"
        );
        RequireLength(message.ReasonText, 1, InspectionFailed.ReasonTextMaxLength, "reasonText");
        return message;
    }

    private static void RequireThemes(IReadOnlyList<ThemeProgress> themes)
    {
        foreach (var theme in themes)
        {
            Require(theme is not null, "a theme must not be null");
            Require(ThemeProgress.Themes.Contains(theme.Theme), "unknown theme");
            Require(theme.ItemsTotal >= 0, "a theme's itemsTotal must not be negative");
            Require(theme.ItemsEvaluated >= 0, "a theme's itemsEvaluated must not be negative");
        }
    }

    private static void RequireQuote(string language, string text, string quote)
    {
        Require(DocumentIndexed.Languages.Contains(language), "unknown language");
        RequireLength(text, 1, 20000, "text");
        RequireLength(quote, 1, 4000, "quote");
    }

    private static void RequireOffsets(int? start, int? end) =>
        Require(start is null or >= 0 && end is null or >= 0, "quote offsets must not be negative");

    /// <summary>Schema lengths count code points, as Python's len does, not UTF-16 units.</summary>
    private static void RequireLength(string value, int min, int max, string field)
    {
        var length = value.EnumerateRunes().Count();
        Require(length >= min && length <= max, $"{field} must be {min} to {max} long");
    }

    /// <summary>The enum converter also reads "License, Report", a value with no name.</summary>
    private static void RequireDefined<TEnum>(TEnum? value, string field)
        where TEnum : struct, Enum =>
        Require(value is not { } named || Enum.IsDefined(named), $"unknown {field}");

    private static void RequireVersionNumber(int number) =>
        Require(number >= 1, "versionNumber must be at least 1");

    private static void Require([DoesNotReturnIf(false)] bool condition, string rule)
    {
        if (!condition)
        {
            throw new MessageFormatException($"The message breaks its schema: {rule}.");
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();
}
