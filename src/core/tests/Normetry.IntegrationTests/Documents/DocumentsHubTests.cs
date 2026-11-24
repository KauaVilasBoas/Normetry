using System.Net;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Documents;
using Normetry.Domain.Documents;
using Normetry.Domain.Identity;
using Normetry.Infrastructure.Messaging;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Documents;

/// <summary>
/// Live status over SignalR, through a real WebSocket to the in-memory API with the token in the
/// query string, as browsers send it. Each test makes its own tenants.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentsHubTests
{
    private readonly DatabaseFixture db;

    public DocumentsHubTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A bound on every wait, so a missing push fails the test instead of hanging it.</summary>
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_client_gets_its_own_establishments_changes_and_never_another_tenants()
    {
        var a = await DocumentStatusTests.UploadAsync(db);
        var b = await DocumentStatusTests.UploadAsync(db);
        await using var clientA = await ConnectAsync(a.Tenant, a.Owner);
        await using var clientB = await ConnectAsync(b.Tenant, b.Owner);
        var receivedA = Listen(clientA);
        var receivedB = Listen(clientB);
        await clientA.InvokeAsync("JoinEstablishment", a.Establishment, Ct).WaitAsync(Wait, Ct);
        await clientB.InvokeAsync("JoinEstablishment", b.Establishment, Ct).WaitAsync(Wait, Ct);

        await DocumentStatusTests.HandleAsync(
            db.Api,
            a.Tenant,
            new DocumentIndexed(a.Establishment, a.Document, a.Version, 1, 4, 2, "en")
        );
        await DocumentStatusTests.HandleAsync(
            db.Api,
            b.Tenant,
            new DocumentIngestionStarted(b.Establishment, b.Document, b.Version, 1)
        );

        var changeA = await receivedA.ReadAsync(Ct).AsTask().WaitAsync(Wait, Ct);
        Assert.Equal(
            (a.Establishment, a.Document, a.Version, DocumentVersionStatus.Indexed, (int?)1),
            (
                changeA.EstablishmentId,
                changeA.DocumentId,
                changeA.VersionId,
                changeA.Status,
                changeA.CurrentVersionNumber
            )
        );
        var changeB = await receivedB.ReadAsync(Ct).AsTask().WaitAsync(Wait, Ct);
        Assert.Equal(
            (b.Version, DocumentVersionStatus.Processing),
            (changeB.VersionId, changeB.Status)
        );
        // Each heard only its own establishment's change.
        await Task.Delay(300, Ct);
        Assert.False(receivedA.TryRead(out _));
        Assert.False(receivedB.TryRead(out _));
    }

    [Fact]
    public async Task A_timed_out_version_reaches_its_own_establishment_and_never_another_tenants()
    {
        var a = await DocumentStatusTests.UploadAsync(db);
        var b = await DocumentStatusTests.UploadAsync(db);
        // Past Documents:ProcessingTimeout (15 minutes).
        await DocumentStatusTests.SetStatusAsync(db, a, "Processing", TimeSpan.FromMinutes(16));
        await DocumentStatusTests.SetStatusAsync(db, b, "Processing", TimeSpan.FromMinutes(16));
        await using var clientA = await ConnectAsync(a.Tenant, a.Owner);
        await using var clientB = await ConnectAsync(b.Tenant, b.Owner);
        var receivedA = Listen(clientA);
        var receivedB = Listen(clientB);
        await clientA.InvokeAsync("JoinEstablishment", a.Establishment, Ct).WaitAsync(Wait, Ct);
        await clientB.InvokeAsync("JoinEstablishment", b.Establishment, Ct).WaitAsync(Wait, Ct);
        var sweeper = ActivatorUtilities.CreateInstance<TimeoutSweeper>(db.Api.Services);

        Assert.Single(await sweeper.SweepDocumentVersionsAsync(a.Tenant, Ct));

        var change = await receivedA.ReadAsync(Ct).AsTask().WaitAsync(Wait, Ct);
        Assert.Equal(
            (
                a.Establishment,
                a.Version,
                DocumentVersionStatus.Failed,
                (string?)DocumentIngestionFailureReasons.TimedOut
            ),
            (change.EstablishmentId, change.VersionId, change.Status, change.FailureCode)
        );
        await Task.Delay(300, Ct);
        Assert.False(receivedB.TryRead(out _));

        // Tenant B's own sweep reaches B, and A hears nothing of it.
        Assert.Single(await sweeper.SweepDocumentVersionsAsync(b.Tenant, Ct));

        var changeB = await receivedB.ReadAsync(Ct).AsTask().WaitAsync(Wait, Ct);
        Assert.Equal(
            (b.Establishment, b.Version, DocumentVersionStatus.Failed),
            (changeB.EstablishmentId, changeB.VersionId, changeB.Status)
        );
        await Task.Delay(300, Ct);
        Assert.False(receivedA.TryRead(out _));
    }

    [Fact]
    public async Task Joining_another_tenants_establishment_is_refused_and_hears_nothing()
    {
        var a = await DocumentStatusTests.UploadAsync(db);
        var b = await DocumentStatusTests.UploadAsync(db);
        await using var intruder = await ConnectAsync(b.Tenant, b.Owner);
        var received = Listen(intruder);

        var refused = await Assert.ThrowsAsync<HubException>(() =>
            intruder.InvokeAsync("JoinEstablishment", a.Establishment, Ct).WaitAsync(Wait, Ct)
        );
        await DocumentStatusTests.HandleAsync(
            db.Api,
            a.Tenant,
            new DocumentIngestionStarted(a.Establishment, a.Document, a.Version, 1)
        );

        Assert.Contains("establishment.not_found", refused.Message);
        await Task.Delay(300, Ct);
        Assert.False(received.TryRead(out _));
    }

    [Fact]
    public async Task Connecting_needs_documents_read()
    {
        var tenant = await TestTenants.CreateAsync(db);
        var reader = Guid.NewGuid().ToString();
        var outsider = Guid.NewGuid().ToString();
        await TestTenants.GrantAsync(db, tenant, reader, Permissions.Documents.Read);
        await TestTenants.GrantAsync(db, tenant, outsider, Permissions.Establishments.Read);

        await using var allowed = await ConnectAsync(tenant, reader);
        Assert.Equal(HubConnectionState.Connected, allowed.State);
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(tenant, outsider));
    }

    [Fact]
    public async Task A_revoked_permission_stops_new_joins_on_an_open_connection()
    {
        var a = await DocumentStatusTests.UploadAsync(db);
        var reader = Guid.NewGuid().ToString();
        var profile = await TestTenants.GrantAsync(
            db,
            a.Tenant,
            reader,
            Permissions.Documents.Read
        );
        await using var connection = await ConnectAsync(a.Tenant, reader);
        await connection.InvokeAsync("JoinEstablishment", a.Establishment, Ct).WaitAsync(Wait, Ct);

        await using (
            var session = await Session.BeginAsync(db.ConnectionString("app_core"), a.Tenant)
        )
        {
            await session.ExecuteAsync(
                "DELETE FROM core.user_profiles WHERE profile_id = $1",
                profile
            );
            await session.CommitAsync();
        }

        await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync("JoinEstablishment", a.Establishment, Ct).WaitAsync(Wait, Ct)
        );
    }

    [Fact]
    public async Task The_query_string_token_is_read_for_the_hubs_only()
    {
        var token = ApiFactory.CreateToken();
        using var client = db.Api.CreateClient(accessToken: null);

        using var response = await client.GetAsync($"/establishments?access_token={token}", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<HubConnection> ConnectAsync(Guid tenant, string sub) =>
        ConnectAsync(db.Api, "hubs/documents", tenant, sub);

    /// <summary>A started connection to <paramref name="path"/> whose WebSocket carries the token in the query string.</summary>
    internal static async Task<HubConnection> ConnectAsync(
        ApiFactory api,
        string path,
        Guid tenant,
        string sub
    )
    {
        var token = ApiFactory.CreateToken(tenant.ToString(), sub);
        var server = api.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, path), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                    await server
                        .CreateWebSocketClient()
                        .ConnectAsync(
                            new UriBuilder(context.Uri) { Query = $"access_token={token}" }.Uri,
                            cancellationToken
                        );
            })
            // The hub sends enums by name ("Indexed"), as the web app reads them.
            .AddJsonProtocol(protocol =>
                protocol.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter())
            )
            .Build();
        try
        {
            await connection.StartAsync(Ct).WaitAsync(Wait, Ct);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        return connection;
    }

    private static ChannelReader<DocumentStatusChange> Listen(HubConnection connection)
    {
        var channel = Channel.CreateUnbounded<DocumentStatusChange>();
        connection.On<DocumentStatusChange>("DocumentStatusChanged", change =>
            channel.Writer.TryWrite(change)
        );
        return channel.Reader;
    }
}
