using DotNet.Testcontainers.Builders;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace Normetry.IntegrationTests.Messaging;

/// <summary>
/// One RabbitMQ broker for the tests that need one, the version the AppHost runs. Without Docker
/// the tests are skipped, except in CI, where they fail.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private RabbitMqContainer? container;
    private string? skipReason;

    public string ConnectionString => container!.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        try
        {
            container = new RabbitMqBuilder("rabbitmq:4.3-alpine").Build();
            await container.StartAsync(TestContext.Current.CancellationToken);
        }
        catch (DockerUnavailableException error)
            when (Environment.GetEnvironmentVariable("CI") is null)
        {
            skipReason = $"Docker is not available: {error.Message}";
        }
    }

    public void SkipWithoutDocker()
    {
        if (skipReason is not null)
        {
            Assert.Skip(skipReason);
        }
    }

    /// <summary>A connection of the test itself, as another service would open one.</summary>
    public Task<IConnection> ConnectAsync() =>
        new ConnectionFactory { Uri = new Uri(ConnectionString) }.CreateConnectionAsync(
            TestContext.Current.CancellationToken
        );

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}
