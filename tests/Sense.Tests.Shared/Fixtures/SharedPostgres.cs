using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Sense.Tests.Shared.Fixtures;

/// <summary>
/// Shared test Postgres server (TK-032). Instead of a Testcontainers container per test instance, every
/// <see cref="TestFixture"/> opens a uniquely-named database on the one server that
/// <c>scripts/test-postgres.sh</c> starts (<c>senseik-test-postgres</c>), addressed via the
/// <see cref="EnvironmentVariableName"/> environment variable. There is no Testcontainers fallback: if the
/// variable is missing, <see cref="GetAdminConnectionString()"/> throws instead of silently starting a container.
/// </summary>
public static class SharedPostgres
{
    /// <summary>Environment variable carrying the admin connection string for the shared Postgres server.</summary>
    public const string EnvironmentVariableName = "SENSE_TEST_POSTGRES";

    /// <summary>
    /// Reads the admin connection string from <see cref="EnvironmentVariableName"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The environment variable is undefined or blank.</exception>
    public static string GetAdminConnectionString() => GetAdminConnectionString(Environment.GetEnvironmentVariable);

    /// <summary>
    /// Same as <see cref="GetAdminConnectionString()"/> but takes the environment-variable reader as a
    /// parameter so unit tests can exercise the missing/blank/present cases without touching the real process
    /// environment (which real <see cref="TestFixture"/> instances may be reading concurrently).
    /// </summary>
    public static string GetAdminConnectionString(Func<string, string?> environmentVariableReader)
    {
        var value = environmentVariableReader(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} tanımlı değil. Önce `scripts/test-postgres.sh` çalıştırın, sonra " +
                $"çıktısındaki `export {EnvironmentVariableName}=...` satırını uygulayıp testleri tekrar başlatın. " +
                "Testcontainers'a geri düşüş yoktur; testler ayrı ayrı konteyner açmaz, tek paylaşımlı sunucuyu kullanır.");
        }

        return value;
    }

    /// <summary>Generates a unique per-test-instance database name: <c>sense_test_&lt;guid N&gt;</c>.</summary>
    public static string CreateDatabaseName() => $"sense_test_{Guid.NewGuid():N}";

    /// <summary>Returns <paramref name="adminConnectionString"/> pointed at <paramref name="databaseName"/> instead.</summary>
    public static string BuildConnectionString(string adminConnectionString, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName };
        return builder.ConnectionString;
    }

    /// <summary>Creates <paramref name="databaseName"/> on the shared server for this test instance's exclusive use.</summary>
    public static async Task CreateDatabaseAsync(
        string adminConnectionString, string databaseName, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Drops <paramref name="databaseName"/>. Clears the Npgsql pool for that database first - otherwise
    /// <c>DROP DATABASE</c> fails while pooled connections are still open against it - then drops with
    /// <c>WITH (FORCE)</c> to disconnect any stragglers (e.g. a background outbox processor) in the same step.
    /// </summary>
    public static async Task DropDatabaseAsync(
        string adminConnectionString, string databaseName, CancellationToken cancellationToken = default)
    {
        var testDatabaseConnectionString = BuildConnectionString(adminConnectionString, databaseName);
        await using (var testConnection = new NpgsqlConnection(testDatabaseConnectionString))
        {
            NpgsqlConnection.ClearPool(testConnection);
        }

        await using var adminConnection = new NpgsqlConnection(adminConnectionString);
        await adminConnection.OpenAsync(cancellationToken);
        await using var command = adminConnection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
