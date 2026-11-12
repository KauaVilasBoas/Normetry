using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using Normetry.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Normetry.IntegrationTests.Database;

/// <summary>
/// One PostgreSQL container for every database test: the real bootstrap from
/// <c>deploy/postgres/init/</c> with random passwords, then the core migrations as
/// <c>core_owner</c>, the way the AppHost's core-migrate step runs them. Tests connect as the real
/// roles, never as the superuser. Without Docker the tests are skipped, except in CI (where
/// <c>CI</c> is set), where they fail instead.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string Image = "pgvector/pgvector:0.8.7-pg17";

    public static readonly string[] Roles =
    [
        "core_owner",
        "ai_owner",
        "app_core",
        "app_ai",
        "corpus_ingestor",
    ];

    private readonly Dictionary<string, string> passwords = Roles.ToDictionary(
        role => role,
        _ => RandomPassword()
    );
    private PostgreSqlContainer? container;
    private string? skipReason;
    private ApiFactory? api;

    /// <summary>The API in memory, connected as <c>app_core</c>, with the demo tenants seeded.</summary>
    public ApiFactory Api => api ?? throw new InvalidOperationException(skipReason);

    public async ValueTask InitializeAsync()
    {
        try
        {
            container = new PostgreSqlBuilder(Image)
                .WithDatabase("normetry")
                .WithUsername("postgres")
                .WithPassword(RandomPassword())
                .WithEnvironment(
                    passwords.ToDictionary(
                        pair => $"{pair.Key.ToUpperInvariant()}_PASSWORD",
                        pair => pair.Value
                    )
                )
                .WithResourceMapping(
                    new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "postgres-init")),
                    "/docker-entrypoint-initdb.d/"
                )
                .Build();
            await container.StartAsync(TestContext.Current.CancellationToken);
        }
        catch (DockerUnavailableException error)
            when (Environment.GetEnvironmentVariable("CI") is null)
        {
            skipReason = $"Docker is not available: {error.Message}";
            return;
        }

        await CoreMigrator.RunAsync(
            ConnectionString("core_owner"),
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );
        api = new ApiFactory { CoreConnectionString = ConnectionString("app_core") };
    }

    /// <summary>Call first in every test (the test class constructor does).</summary>
    public void SkipWithoutDocker()
    {
        if (skipReason is not null)
        {
            Assert.Skip(skipReason);
        }
    }

    public string ConnectionString(string role) =>
        new NpgsqlConnectionStringBuilder(container!.GetConnectionString())
        {
            Username = role,
            Password = passwords[role],
        }.ConnectionString;

    /// <summary>The container superuser: only for tests that prove the API refuses it.</summary>
    public string SuperuserConnectionString => container!.GetConnectionString();

    public async ValueTask DisposeAsync()
    {
        if (api is not null)
        {
            await api.DisposeAsync();
        }
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    private static string RandomPassword() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
