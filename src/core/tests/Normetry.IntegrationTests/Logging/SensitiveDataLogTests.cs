using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Inspections;
using Normetry.Domain.Documents;
using Normetry.Domain.Inspections;
using Normetry.Infrastructure.Messaging;
using Normetry.IntegrationTests.Database;
using Normetry.IntegrationTests.Documents;
using Normetry.IntegrationTests.Inspections;
using Xunit;

namespace Normetry.IntegrationTests.Logging;

/// <summary>
/// Logs and trace attributes carry ids, codes and counts, never document data: a title, an
/// original file name, a quote, a rationale or a reason text. The marker name runs through an
/// upload and through the status and inspection consumers; no captured line or tag may hold it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SensitiveDataLogTests
{
    private const string Marker = "Zephyrine Quolmsby";

    private static readonly ActivitySource Consumer = new("Normetry.Tests.Consumer");

    private readonly DatabaseFixture db;

    public SensitiveDataLogTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uploads_and_status_and_inspection_events_log_no_document_data()
    {
        var logs = new CapturingLoggerProvider();
        var tags = new ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => tags.Enqueue(Describe(activity)),
        };
        ActivitySource.AddActivityListener(listener);
        // Every level, Trace included, whatever appsettings.json filters out.
        await using var api = db.Api.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging =>
                logging.AddProvider(logs).AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace)
            )
        );

        // An upload whose title and file name hold the marker, then two refused ones.
        var owner = Guid.NewGuid().ToString();
        var tenant = await TestTenants.CreateAsync(db, null, owner);
        var establishment = await TestTenants.CreateEstablishmentAsync(db, tenant);
        using var client = api.CreateClient(ApiFactory.CreateToken(tenant.ToString(), owner));
        Guid document,
            version;
        using (
            var upload = await DocumentEndpointTests.UploadAsync(
                client,
                establishment,
                $"{Marker} license.pdf",
                "application/pdf",
                SampleFiles.Pdf,
                $"Food handler license of {Marker}",
                "License"
            )
        )
        {
            Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
            var body = await upload.Content.ReadFromJsonAsync<JsonElement>(Ct);
            document = body.GetProperty("id").GetGuid();
            version = body.GetProperty("versions")[0].GetProperty("id").GetGuid();
        }
        using (
            var notAPdf = await DocumentEndpointTests.UploadAsync(
                client,
                establishment,
                $"{Marker}.pdf",
                "application/pdf",
                Encoding.UTF8.GetBytes(Marker),
                Marker
            )
        )
        {
            Assert.Equal(HttpStatusCode.BadRequest, notAPdf.StatusCode);
        }
        using (
            var titleTooLong = await DocumentEndpointTests.UploadAsync(
                client,
                establishment,
                $"{Marker}.pdf",
                "application/pdf",
                SampleFiles.Pdf,
                string.Concat(Enumerable.Repeat(Marker, 20))
            )
        )
        {
            Assert.Equal(HttpStatusCode.BadRequest, titleTooLong.StatusCode);
        }
        Assert.Contains(Marker, await client.GetStringAsync($"/documents/{document}", Ct));

        // The document status consumer: the version starts and fails with a reason holding it.
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync<DocumentStatusMessages>(
                api,
                tenant,
                new DocumentIngestionStarted(establishment, document, version, 1)
            )
        );
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync<DocumentStatusMessages>(
                api,
                tenant,
                new DocumentIngestionFailed(
                    establishment,
                    document,
                    version,
                    1,
                    "parse_error",
                    $"The file of {Marker} could not be read."
                )
            )
        );

        // The inspection consumer: one inspection completes with the marker in every text, one
        // cites a document of another establishment (refused, logged), and a body off the
        // contract is dead-lettered (logged).
        var completed = await TestTenants.NewTargetAsync(db);
        var refused = await TestTenants.NewTargetAsync(db);
        var completedId = await StartAsync(api, completed);
        var refusedId = await StartAsync(api, refused);
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync<InspectionStatusMessages>(
                api,
                completed.Tenant,
                new InspectionStarted(
                    completed.Establishment,
                    completedId,
                    1,
                    [new("temperature_control", 1, 0)]
                )
            )
        );
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync<InspectionStatusMessages>(
                api,
                completed.Tenant,
                Completion(completed.Establishment, completedId, completed.Establishment)
            )
        );
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync<InspectionStatusMessages>(
                api,
                refused.Tenant,
                Completion(refused.Establishment, refusedId, Guid.NewGuid())
            )
        );
        Assert.Equal(
            ConsumeOutcome.Ack,
            await HandleAsync<InspectionStatusMessages>(
                api,
                refused.Tenant,
                new InspectionFailed(refused.Establishment, refusedId, "error", Marker)
            )
        );
        var tooLong = Completion(completed.Establishment, completedId, completed.Establishment);
        Assert.Equal(
            ConsumeOutcome.DeadLetter,
            await HandleAsync<InspectionStatusMessages>(
                api,
                completed.Tenant,
                tooLong with
                {
                    Findings =
                    [
                        tooLong.Findings[0] with
                        {
                            Rationale = string.Concat(Enumerable.Repeat(Marker, 300)),
                        },
                    ],
                }
            )
        );
        using var inspectionClient = InspectionEndpointTests.Client(api, completed);
        Assert.Contains(
            Marker,
            await inspectionClient.GetStringAsync(
                $"{InspectionEndpointTests.StartPath(completed)}/{completedId}",
                Ct
            )
        );

        // "Quolmsby" alone also catches an encoded space ("Zephyrine%20Quolmsby").
        Assert.DoesNotContain(logs.Lines, line =>
            line.Contains("Quolmsby", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(tags, tag =>
            tag.Contains("Quolmsby", StringComparison.OrdinalIgnoreCase)
        );
        // The capture works: the handlers logged, and the consumer's span got the tenant.
        Assert.Contains(logs.Lines, line => line.Contains("UploadDocumentCommand handled"));
        Assert.Contains(logs.Lines, line => line.Contains("is refused"));
        Assert.Contains(logs.Lines, line => line.Contains("Dead-lettering"));
        Assert.Contains(tags, tag => tag.Contains($"normetry.tenant_id={completed.Tenant}"));
    }

    private static async Task<Guid> StartAsync(
        WebApplicationFactory<Program> api,
        TestTenants.Target target
    )
    {
        using var client = InspectionEndpointTests.Client(api, target);
        return await InspectionEndpointTests.StartAsync(client, target);
    }

    /// <summary>
    /// The body the AI service would publish, handled as the consumer does, inside a consumer
    /// span so the handler's tags land somewhere.
    /// </summary>
    private static async Task<ConsumeOutcome> HandleAsync<THandler>(
        WebApplicationFactory<Program> api,
        Guid tenant,
        IIntegrationMessage payload
    )
        where THandler : class
    {
        using var span = Consumer.StartActivity("consume", ActivityKind.Consumer);
        var body = payload switch
        {
            DocumentIngestionStarted message => DocumentStatusTests.Body(tenant, message),
            DocumentIngestionFailed message => DocumentStatusTests.Body(tenant, message),
            InspectionStarted message => DocumentStatusTests.Body(tenant, message),
            InspectionCompleted message => DocumentStatusTests.Body(tenant, message),
            InspectionFailed message => DocumentStatusTests.Body(tenant, message),
            _ => throw new ArgumentOutOfRangeException(nameof(payload)),
        };
        return api.Services.GetRequiredService<THandler>() switch
        {
            DocumentStatusMessages documents => await documents.HandleAsync(body, Ct),
            InspectionStatusMessages inspections => await inspections.HandleAsync(body, Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(THandler)),
        };
    }

    /// <summary>One non-compliant finding with the marker in each of its texts.</summary>
    private static InspectionCompleted Completion(
        Guid establishment,
        Guid inspection,
        Guid evidenceDocument
    ) =>
        new(
            establishment,
            inspection,
            1200,
            300,
            0.01m,
            [
                new(
                    "us-temp-01",
                    "temperature_control",
                    $"Cold holding checked by {Marker}",
                    "critical",
                    "non_compliant",
                    "high",
                    $"{Marker} logged 45°F on the cooler.",
                    [
                        new(
                            "FDA Food Code",
                            "2022",
                            "3-501.16",
                            "en",
                            $"Cold holding at 41°F, says {Marker}.",
                            Marker,
                            null,
                            null
                        ),
                    ],
                    [
                        new(
                            evidenceDocument,
                            $"Cooler log of {Marker}",
                            DocumentType.Log,
                            1,
                            $"Week of {Marker}",
                            "en",
                            $"Reading 45°F, signed {Marker}.",
                            Marker,
                            null,
                            null
                        ),
                    ],
                    new($"{Marker} recalibrates the cooler.", 7)
                ),
            ]
        );

    private static string Describe(Activity activity) =>
        string.Join(
            " | ",
            [
                activity.DisplayName,
                activity.OperationName,
                activity.StatusDescription ?? "",
                .. activity.TagObjects.Select(tag => $"{tag.Key}={Describe(tag.Value)}"),
                .. activity.Events.SelectMany(@event =>
                    @event
                        .Tags.Select(tag => $"{tag.Key}={Describe(tag.Value)}")
                        .Prepend(@event.Name)
                ),
                .. activity.Baggage.Select(item => $"{item.Key}={item.Value}"),
            ]
        );

    /// <summary>A value as an exporter would write it, collections and structured fields included.</summary>
    private static string Describe(object? value) =>
        value switch
        {
            null => "",
            string text => text,
            IEnumerable<KeyValuePair<string, object?>> fields => string.Join(
                ", ",
                fields.Select(field => $"{field.Key}={Describe(field.Value)}")
            ),
            IEnumerable items => string.Join(", ", items.Cast<object?>().Select(Describe)),
            _ => value.ToString() ?? "",
        };

    /// <summary>Every log line with its message, structured fields, scopes and exception.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Lines);

        public void Dispose() { }

        private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                lines.Enqueue($"{category} scope {Describe(state)}");
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) =>
                lines.Enqueue(
                    $"{category} {formatter(state, exception)} | {Describe(state)} | {exception}"
                );
        }
    }
}
