using System.Text.Json;
using System.Text.RegularExpressions;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;

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
            DocumentIngestionFailed.ReasonCodes.Contains(message.ReasonCode),
            "unknown reasonCode"
        );
        Require(
            message.ReasonText.Length is >= 1 and <= DocumentIngestionFailed.ReasonTextMaxLength,
            "reasonText must be 1 to 500 long"
        );
        return message;
    }

    private static void RequireVersionNumber(int number) =>
        Require(number >= 1, "versionNumber must be at least 1");

    private static void Require(bool condition, string rule)
    {
        if (!condition)
        {
            throw new MessageFormatException($"The message breaks its schema: {rule}.");
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();
}
