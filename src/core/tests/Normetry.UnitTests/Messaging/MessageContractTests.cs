using System.Text;
using System.Text.Json;
using Json.Schema;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Infrastructure.Messaging;
using Xunit;

namespace Normetry.UnitTests.Messaging;

/// <summary>
/// The core's side of <c>src/shared/contracts/messages</c> (the Python side is
/// <c>src/ai/tests/test_contracts.py</c>): the schemas, the shared fixtures, the C# message types
/// and what the core writes must all agree.
/// </summary>
public sealed class MessageContractTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "contracts");

    private static readonly Lazy<IReadOnlyDictionary<string, JsonSchema>> Schemas = new(Load);

    public static TheoryData<string> Messages =>
        [
            "document-uploaded.v1",
            "document-deleted.v1",
            "document-ingestion-started.v1",
            "document-indexed.v1",
            "document-ingestion-failed.v1",
        ];

    [Fact]
    public void Every_schema_is_valid_JSON_Schema_2020_12()
    {
        var files = Directory.GetFiles(Folder, "*.schema.json");

        Assert.Equal(6, files.Length); // the envelope and the five messages
        Assert.All(files, file =>
            Assert.True(
                MetaSchemas.Draft202012.Evaluate(Parse(File.ReadAllText(file))).IsValid,
                Path.GetFileName(file)
            )
        );
    }

    [Fact]
    public void Every_message_type_of_the_core_has_a_schema()
    {
        var types = typeof(IIntegrationMessage)
            .Assembly.GetTypes()
            .Where(type => type.IsAssignableTo(typeof(IIntegrationMessage)) && !type.IsInterface)
            .Select(type =>
                (string)type.GetProperty(nameof(IIntegrationMessage.Type))!.GetValue(null)!
            )
            .Order()
            .ToArray();

        Assert.Equal(Messages.Select(row => Pascal(row.Data)).Order().ToArray(), types);
    }

    [Theory]
    [MemberData(nameof(Messages))]
    public void The_valid_fixture_matches_its_schema_and_the_core_reads_it(string message)
    {
        var body = Fixture(message, "valid");

        Assert.True(Schemas.Value[message].Evaluate(Parse(body)).IsValid);
        var read = MessageJson.Parse(Encoding.UTF8.GetBytes(body));
        Assert.Equal(Pascal(message), read.Type);
        Assert.Equal(Pascal(message), read.Payload.GetType().Name);
    }

    [Theory]
    [MemberData(nameof(Messages))]
    public void The_invalid_fixture_breaks_its_schema_and_the_core_refuses_it(string message)
    {
        var body = Fixture(message, "invalid");

        Assert.False(Schemas.Value[message].Evaluate(Parse(body)).IsValid);
        Assert.Throws<MessageFormatException>(() =>
            MessageJson.Parse(Encoding.UTF8.GetBytes(body))
        );
    }

    [Theory]
    [MemberData(nameof(Messages))]
    public void What_the_core_writes_matches_the_schema_and_reads_back_the_same(string message)
    {
        var read = MessageJson.Parse(Encoding.UTF8.GetBytes(Fixture(message, "valid")));

        var written = Write(read);

        Assert.True(Schemas.Value[message].Evaluate(Parse(written)).IsValid, written);
        Assert.Equal(read, MessageJson.Parse(Encoding.UTF8.GetBytes(written)));
    }

    [Fact]
    public void A_null_page_count_is_written_as_null()
    {
        var written = MessageJson.Serialize(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            new DocumentIndexed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 3, null, "en")
        );

        Assert.True(Schemas.Value["document-indexed.v1"].Evaluate(Parse(written)).IsValid);
        Assert.Contains("\"pageCount\":null", written);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData(
        """{"messageId":"0b8f6c3e-2d1a-4e5f-9a8b-7c6d5e4f3a21","type":"DocumentDeleted","version":1,"occurredAt":"2026-11-16T12:00:00Z","TenantId":"3f0c2a9e-6b1d-4c8e-9a57-2d4e8b1c7f60","payload":{"establishmentId":"8a1e5b77-0c3d-4f29-b6a4-91d2e3c4f5a6","documentId":"c4b2a1f0-9e8d-4c7b-a6f5-e4d3c2b1a0f9"}}"""
    )]
    [InlineData(
        """{"messageId":"0b8f6c3e-2d1a-4e5f-9a8b-7c6d5e4f3a21","type":"DocumentArchived","version":1,"occurredAt":"2026-11-16T12:00:00Z","tenantId":"3f0c2a9e-6b1d-4c8e-9a57-2d4e8b1c7f60","payload":{}}"""
    )]
    [InlineData(
        """{"messageId":"0b8f6c3e-2d1a-4e5f-9a8b-7c6d5e4f3a21","type":"DocumentDeleted","version":2,"occurredAt":"2026-11-16T12:00:00Z","tenantId":"3f0c2a9e-6b1d-4c8e-9a57-2d4e8b1c7f60","payload":{}}"""
    )]
    public void A_body_off_the_contract_is_refused(string body) =>
        Assert.Throws<MessageFormatException>(() =>
            MessageJson.Parse(Encoding.UTF8.GetBytes(body))
        );

    private static string Write(InboundMessage read) =>
        read.Payload switch
        {
            DocumentUploaded payload => Serialize(read, payload),
            DocumentDeleted payload => Serialize(read, payload),
            DocumentIngestionStarted payload => Serialize(read, payload),
            DocumentIndexed payload => Serialize(read, payload),
            DocumentIngestionFailed payload => Serialize(read, payload),
            _ => throw new InvalidOperationException(read.Type),
        };

    private static string Serialize<TMessage>(InboundMessage read, TMessage payload)
        where TMessage : IIntegrationMessage =>
        MessageJson.Serialize(read.MessageId, read.TenantId, read.OccurredAt, payload);

    private static IReadOnlyDictionary<string, JsonSchema> Load()
    {
        // The message schemas refer to the envelope by relative $ref, so it is registered first.
        var options = new BuildOptions { SchemaRegistry = new() };
        var schemas = new Dictionary<string, JsonSchema>
        {
            ["envelope.v1"] = JsonSchema.FromText(
                File.ReadAllText(Path.Combine(Folder, "envelope.v1.schema.json")),
                options
            ),
        };
        foreach (var message in Messages.Select(row => row.Data))
        {
            schemas[message] = JsonSchema.FromText(
                File.ReadAllText(Path.Combine(Folder, $"{message}.schema.json")),
                options
            );
        }
        return schemas;
    }

    private static string Fixture(string message, string kind) =>
        File.ReadAllText(Path.Combine(Folder, "fixtures", $"{message}.{kind}.json"));

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary><c>document-ingestion-started.v1</c> to <c>DocumentIngestionStarted</c>.</summary>
    private static string Pascal(string message) =>
        string.Concat(
            message[..message.LastIndexOf('.')]
                .Split('-')
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..])
        );
}
