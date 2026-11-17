using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Normetry.Application.Inspections;
using Normetry.Domain.Identity;
using Normetry.Domain.Inspections;
using Normetry.IntegrationTests.Database;
using Normetry.IntegrationTests.Documents;
using Xunit;
using static Normetry.IntegrationTests.Inspections.InspectionEndpointTests;

namespace Normetry.IntegrationTests.Inspections;

/// <summary>
/// Live inspection progress over SignalR, through a real WebSocket to the in-memory API with the
/// token in the query string. Each test makes its own tenants.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InspectionsHubTests
{
    private const string Path = "hubs/inspections";

    private readonly DatabaseFixture db;

    public InspectionsHubTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TimeSpan Wait => DocumentsHubTests.Wait;

    [Fact]
    public async Task A_client_hears_its_own_establishments_inspections_and_never_another_tenants()
    {
        var a = await NewTargetAsync(db);
        var b = await NewTargetAsync(db);
        using var clientA = Client(db.Api, a);
        using var clientB = Client(db.Api, b);
        var (inspectionA, inspectionB) = (
            await StartAsync(clientA, a),
            await StartAsync(clientB, b)
        );
        await using var hubA = await ConnectAsync(a);
        await using var hubB = await ConnectAsync(b);
        var receivedA = Listen(hubA);
        var receivedB = Listen(hubB);
        await hubA.InvokeAsync("JoinEstablishment", a.Establishment, Ct).WaitAsync(Wait, Ct);
        await hubB.InvokeAsync("JoinEstablishment", b.Establishment, Ct).WaitAsync(Wait, Ct);

        await HandleAsync(
            db.Api,
            a.Tenant,
            new InspectionProgressed(
                a.Establishment,
                inspectionA,
                2,
                5,
                [new("temperature_control", 5, 2)]
            )
        );
        await HandleAsync(
            db.Api,
            b.Tenant,
            new InspectionFailed(b.Establishment, inspectionB, "budget_exceeded", "Too costly.")
        );

        var changeA = await receivedA.ReadAsync(Ct).AsTask().WaitAsync(Wait, Ct);
        Assert.Equal(
            (inspectionA, a.Establishment, InspectionStatus.Running, 2, 5),
            (
                changeA.InspectionId,
                changeA.EstablishmentId,
                changeA.Status,
                changeA.ItemsEvaluated,
                changeA.ItemsTotal
            )
        );
        Assert.Equal([new ThemeProgress("temperature_control", 5, 2)], changeA.Themes);
        var changeB = await receivedB.ReadAsync(Ct).AsTask().WaitAsync(Wait, Ct);
        Assert.Equal(
            (inspectionB, InspectionStatus.Failed, "budget_exceeded"),
            (changeB.InspectionId, changeB.Status, changeB.FailureReasonCode)
        );
        // An event that changes nothing pushes nothing, and each heard only its own change.
        await HandleAsync(
            db.Api,
            a.Tenant,
            new InspectionProgressed(a.Establishment, inspectionA, 1, 5, [])
        );
        await Task.Delay(300, Ct);
        Assert.False(receivedA.TryRead(out _));
        Assert.False(receivedB.TryRead(out _));
    }

    [Fact]
    public async Task Joining_another_tenants_establishment_is_refused_and_hears_nothing()
    {
        var a = await NewTargetAsync(db);
        var b = await NewTargetAsync(db);
        using var clientA = Client(db.Api, a);
        var inspection = await StartAsync(clientA, a);
        await using var intruder = await ConnectAsync(b);
        var received = Listen(intruder);

        var refused = await Assert.ThrowsAsync<HubException>(() =>
            intruder.InvokeAsync("JoinEstablishment", a.Establishment, Ct).WaitAsync(Wait, Ct)
        );
        await HandleAsync(
            db.Api,
            a.Tenant,
            new InspectionStarted(a.Establishment, inspection, 5, [])
        );

        Assert.Contains("establishment.not_found", refused.Message);
        await Task.Delay(300, Ct);
        Assert.False(received.TryRead(out _));
    }

    [Fact]
    public async Task Connecting_needs_inspections_read()
    {
        var tenant = await TestTenants.CreateAsync(db);
        var reader = Guid.NewGuid().ToString();
        var outsider = Guid.NewGuid().ToString();
        await TestTenants.GrantAsync(db, tenant, reader, Permissions.Inspections.Read);
        await TestTenants.GrantAsync(db, tenant, outsider, Permissions.Documents.Read);

        await using var allowed = await DocumentsHubTests.ConnectAsync(
            db.Api,
            Path,
            tenant,
            reader
        );
        Assert.Equal(HubConnectionState.Connected, allowed.State);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            DocumentsHubTests.ConnectAsync(db.Api, Path, tenant, outsider)
        );
    }

    private Task<HubConnection> ConnectAsync(Target target) =>
        DocumentsHubTests.ConnectAsync(db.Api, Path, target.Tenant, target.Owner);

    private static ChannelReader<InspectionChange> Listen(HubConnection connection)
    {
        var channel = Channel.CreateUnbounded<InspectionChange>();
        connection.On<InspectionChange>("InspectionChanged", change =>
            channel.Writer.TryWrite(change)
        );
        return channel.Reader;
    }
}
