using Normetry.Migrations;

// Short-lived: the AppHost runs it as core_owner before the API starts, and it exits when done.
// No other process gets the owner credentials, because the owner can switch RLS off.
var connectionString = Environment.GetEnvironmentVariable(CoreMigrator.ConnectionStringVariable)
    ?? throw new InvalidOperationException(
        $"Set {CoreMigrator.ConnectionStringVariable} (the core_owner role)."
    );

await CoreMigrator.RunAsync(connectionString, Console.Out, CancellationToken.None);
