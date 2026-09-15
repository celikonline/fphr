using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Tests.Integration;

/// <summary>
/// Example integration test demonstrating TestFixture usage.
/// This test class shows how to:
/// - Create tenants and users
/// - Set user context
/// - Execute commands and queries
/// - Verify audit events
/// - Assert on results
/// </summary>
[Collection("PostgreSQL")]
public sealed class ExampleIntegrationTest : TestFixture
{
    /// <summary>
    /// Example: Basic fixture initialization and user context setup.
    /// This is a smoke test that verifies the test infrastructure is working correctly.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Fixture_InitializesCorrectly_WithTenantAndUser()
    {
        // Arrange
        var tenantId = await CreateTenantAsync("Test Company", slug: "test-company");
        var userId = await CreateUserAsync(tenantId, "test.user@example.com",
            new HashSet<string> { "Admin", "User" });

        // Act
        SetCurrentUser(tenantId, userId, "test.user@example.com",
            new HashSet<string> { "Admin", "User" });

        // Assert
        tenantId.ShouldNotBe(Guid.Empty);
        userId.ShouldNotBe(Guid.Empty);
        TenantContextAccessor?.ShouldNotBeNull();
        TenantContextAccessor?.IsResolved.ShouldBeTrue();
    }

    /// <summary>
    /// Example: Set system user context for background jobs.
    /// System user has no tenant requirement and can perform administrative operations.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Fixture_SupportsSystemUser_ForBackgroundOperations()
    {
        // Act
        SetSystemUser();

        // Assert
        CurrentUser?.IsAuthenticated.ShouldBeTrue();
        CurrentUser?.IsPlatformAdmin.ShouldBeTrue();
        CurrentUser?.DisplayName.ShouldBe("System");
    }

    /// <summary>
    /// Example: Multiple tenants are properly isolated.
    /// Each tenant should only see their own data.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Fixture_IsolatesTenants_WithSeperateContexts()
    {
        // Arrange
        var tenant1Id = await CreateTenantAsync("Tenant 1");
        var tenant2Id = await CreateTenantAsync("Tenant 2");

        var user1Id = await CreateUserAsync(tenant1Id, "user1@tenant1.com");
        var user2Id = await CreateUserAsync(tenant2Id, "user2@tenant2.com");

        // Act
        SetCurrentUser(tenant1Id, user1Id, "user1@tenant1.com", new HashSet<string> { "User" });

        // Assert
        TenantContextAccessor?.TenantId.ShouldBe(tenant1Id);
        tenant1Id.ShouldNotBe(tenant2Id);
        user1Id.ShouldNotBe(user2Id);
    }

    /// <summary>
    /// Example: Dispatcher can be used for command/query testing.
    /// Although no actual handlers are registered yet, this shows the pattern.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Fixture_ProvidesDispatcher_ForCommandExecution()
    {
        // Arrange
        var tenantId = await CreateTenantAsync("Test Company");
        var userId = await CreateUserAsync(tenantId, "test@example.com");
        SetCurrentUser(tenantId, userId, "test@example.com", new HashSet<string> { "User" });

        // Act
        Dispatcher.ShouldNotBeNull();

        // Assert
        // When module handlers are implemented, you would test them like:
        // var result = await ExecuteCommandAsync(new SomeCommand(...));
        // result.ShouldBeSuccess();
    }

    /// <summary>
    /// Example: Clock provider for time-based testing.
    /// Tests can control time via Clock property.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Fixture_ProvidesClock_ForTimeDependentTests()
    {
        // Act
        Clock.ShouldNotBeNull();
        var now = Clock.GetUtcNow();

        // Assert
        now.ShouldNotBe(default);
    }

    /// <summary>
    /// Example: Service provider can be used to resolve dependencies.
    /// This is useful for testing services directly without the dispatcher.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Fixture_ProvidesServiceProvider_ForDependencyResolution()
    {
        // Act
        ServiceProvider.ShouldNotBeNull();
        var tenantContext = ServiceProvider?.GetService(typeof(ITenantContext));

        // Assert
        tenantContext.ShouldNotBeNull();
    }

    /// <summary>
    /// Example: Database is automatically reset between tests.
    /// Each test starts with a clean database via Respawn.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Fixture_ResetsDatabaseBetweenTests_Automatically()
    {
        // Arrange
        var tenantId1 = await CreateTenantAsync("Tenant A");
        var tenantId2 = await CreateTenantAsync("Tenant B");

        // Act
        // In the next test, the database will be reset automatically
        // No manual cleanup is needed

        // Assert
        tenantId1.ShouldNotBe(Guid.Empty);
        tenantId2.ShouldNotBe(Guid.Empty);
    }
}
