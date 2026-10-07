using System;
using System.Collections.Generic;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>
/// Unit tests for <see cref="SharedPostgres"/> (TK-032): reading the shared server's admin connection string
/// from the <c>SENSE_TEST_POSTGRES</c> environment variable, the no-fallback error, and unique database-name
/// generation. These exercise the environment-variable-reader overload so they never touch the real process
/// environment, which real <see cref="TestFixture"/> instances elsewhere in the suite may be reading concurrently
/// - no actual Postgres connection is needed. The class-level trait is only to satisfy this assembly's
/// <see cref="IntegrationTraitConventionTests"/> (every test in <c>Sense.Tests.Integration</c> must carry it).
/// </summary>
[Trait("Category", "Integration")]
public sealed class SharedPostgresTests
{
    private const string ExpectedConnectionString = "Host=127.0.0.1;Port=55432;Username=postgres;Password=postgres;Database=postgres";

    [Fact]
    public void GetAdminConnectionString_ReturnsValue_WhenEnvironmentVariableIsSet()
    {
        // Arrange
        Func<string, string?> reader = name =>
            name == SharedPostgres.EnvironmentVariableName ? ExpectedConnectionString : null;

        // Act
        var result = SharedPostgres.GetAdminConnectionString(reader);

        // Assert
        result.ShouldBe(ExpectedConnectionString);
    }

    [Fact]
    public void GetAdminConnectionString_ThrowsInvalidOperationException_WhenEnvironmentVariableIsUndefined()
    {
        // Arrange
        Func<string, string?> reader = _ => null;

        // Act
        var exception = Should.Throw<InvalidOperationException>(() => SharedPostgres.GetAdminConnectionString(reader));

        // Assert
        exception.Message.ShouldContain(SharedPostgres.EnvironmentVariableName);
        exception.Message.ShouldContain("scripts/test-postgres.sh");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetAdminConnectionString_ThrowsInvalidOperationException_WhenEnvironmentVariableIsBlank(string blankValue)
    {
        // Arrange
        Func<string, string?> reader = _ => blankValue;

        // Act
        var exception = Should.Throw<InvalidOperationException>(() => SharedPostgres.GetAdminConnectionString(reader));

        // Assert
        exception.Message.ShouldContain(SharedPostgres.EnvironmentVariableName);
    }

    [Fact]
    public void GetAdminConnectionString_DoesNotFallBackToTestcontainers_ItOnlyReadsTheGivenReader()
    {
        // Arrange: a reader that only recognizes an unrelated variable name proves there is no other source
        // (e.g. a hardcoded Testcontainers connection) the method could silently fall back to.
        var seen = new List<string>();
        Func<string, string?> reader = name =>
        {
            seen.Add(name);
            return null;
        };

        // Act
        Should.Throw<InvalidOperationException>(() => SharedPostgres.GetAdminConnectionString(reader));

        // Assert
        seen.ShouldBe(new[] { SharedPostgres.EnvironmentVariableName });
    }

    [Fact]
    public void CreateDatabaseName_StartsWithSenseTestPrefix_FollowedByGuidNFormat()
    {
        // Act
        var name = SharedPostgres.CreateDatabaseName();

        // Assert
        name.ShouldStartWith("sense_test_");
        var suffix = name["sense_test_".Length..];
        suffix.Length.ShouldBe(32);
        Guid.TryParseExact(suffix, "N", out _).ShouldBeTrue();
    }

    [Fact]
    public void CreateDatabaseName_ReturnsAUniqueNameOnEachCall()
    {
        // Act
        var first = SharedPostgres.CreateDatabaseName();
        var second = SharedPostgres.CreateDatabaseName();

        // Assert
        first.ShouldNotBe(second);
    }

    [Fact]
    public void BuildConnectionString_ReplacesDatabase_KeepingHostCredentials()
    {
        // Arrange
        const string adminConnectionString = "Host=127.0.0.1;Port=55432;Username=postgres;Password=postgres;Database=postgres";

        // Act
        var result = SharedPostgres.BuildConnectionString(adminConnectionString, "sense_test_abc123");

        // Assert
        result.ShouldContain("Database=sense_test_abc123");
        result.ShouldContain("Host=127.0.0.1");
        result.ShouldContain("Username=postgres");
    }
}
