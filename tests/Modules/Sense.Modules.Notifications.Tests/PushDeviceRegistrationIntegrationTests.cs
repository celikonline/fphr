using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Notifications.Application.PushDevices;
using Sense.Modules.Notifications.Domain.PushDevices;
using Sense.Modules.Notifications.Infrastructure;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Notifications.Tests;

/// <summary>
/// RegisterMyPushDeviceCommand / RevokeMyPushDeviceCommand through the real Dispatcher pipeline (Authorization,
/// Validation, UnitOfWork) against real PostgreSQL (Testcontainers, push-bildirimleri-tasarim.md §2.1/§2.2):
/// idempotent upsert, cross-owner (and cross-tenant - a token belongs to one physical device, not a tenant, see
/// PushDeviceToken remarks) handover, and a no-op revoke for a token the caller does not own.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class PushDeviceRegistrationIntegrationTests : TestFixture
{
    private const string Token = "ExponentPushToken[abc123]";

    protected override void ConfigureServices(IServiceCollection services)
    {
        var baseConfiguration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(baseConfiguration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://app.senseik.test",
                ["Email:Provider"] = "None",
                ["Notifications:DefaultLanguage"] = "tr",
            })
            .Build();

        services.AddNotificationsCore(configuration);
        services.AddSingleton<IPermissionService, GrantAllPermissions>();
        services.AddSingleton<IDataScopeService, UnrestrictedDataScope>();
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task Register_SameTokenSameUser_IsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        SetCurrentUser(tenantId, userId, "user@acme.test", new HashSet<string>());

        (await ExecuteCommandAsync(new RegisterMyPushDeviceCommand(Token, PushPlatforms.Ios, "iPhone"))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new RegisterMyPushDeviceCommand(Token, PushPlatforms.Ios, "iPhone"))).IsSuccess.ShouldBeTrue();

        var rows = await AllDevicesAsync();
        var row = rows.ShouldHaveSingleItem();
        row.UserId.ShouldBe(userId);
        row.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Register_TokenHandedToAnotherUserInAnotherTenant_RevokesOldRow_AndCreatesNewActiveRow()
    {
        var firstTenantId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();

        SetCurrentUser(firstTenantId, firstUserId, "first@acme.test", new HashSet<string>());
        (await ExecuteCommandAsync(new RegisterMyPushDeviceCommand(Token, PushPlatforms.Android, null))).IsSuccess.ShouldBeTrue();

        // Logout, device wiped, a different employee (different tenant) logs into the same physical device.
        SetCurrentUser(secondTenantId, secondUserId, "second@other-tenant.test", new HashSet<string>());
        (await ExecuteCommandAsync(new RegisterMyPushDeviceCommand(Token, PushPlatforms.Android, null))).IsSuccess.ShouldBeTrue();

        var rows = await AllDevicesAsync();
        rows.Count.ShouldBe(2);

        var oldRow = rows.Single(r => r.UserId == firstUserId);
        oldRow.IsActive.ShouldBeFalse();
        oldRow.TenantId.ShouldBe(firstTenantId);

        var newRow = rows.Single(r => r.UserId == secondUserId);
        newRow.IsActive.ShouldBeTrue();
        newRow.TenantId.ShouldBe(secondTenantId);
    }

    [Fact]
    public async Task Revoke_OwnToken_MarksItRevoked()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        SetCurrentUser(tenantId, userId, "user@acme.test", new HashSet<string>());
        (await ExecuteCommandAsync(new RegisterMyPushDeviceCommand(Token, PushPlatforms.Ios, null))).IsSuccess.ShouldBeTrue();

        (await ExecuteCommandAsync(new RevokeMyPushDeviceCommand(Token))).IsSuccess.ShouldBeTrue();

        var row = (await AllDevicesAsync()).ShouldHaveSingleItem();
        row.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Revoke_TokenOwnedByAnotherUser_IsNoOp()
    {
        var ownerTenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        SetCurrentUser(ownerTenantId, ownerUserId, "owner@acme.test", new HashSet<string>());
        (await ExecuteCommandAsync(new RegisterMyPushDeviceCommand(Token, PushPlatforms.Ios, null))).IsSuccess.ShouldBeTrue();

        var otherTenantId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        SetCurrentUser(otherTenantId, otherUserId, "other@acme.test", new HashSet<string>());
        var result = await ExecuteCommandAsync(new RevokeMyPushDeviceCommand(Token));

        result.IsSuccess.ShouldBeTrue();
        var row = (await AllDevicesAsync()).ShouldHaveSingleItem();
        row.UserId.ShouldBe(ownerUserId);
        row.IsActive.ShouldBeTrue();
    }

    private async Task<List<PushDeviceToken>> AllDevicesAsync()
    {
        using var scope = ServiceProvider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().PushDeviceTokens
            .IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    private sealed class GrantAllPermissions : IPermissionService
    {
        public Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<DataScope> GetScopeAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(DataScope.Company);
    }

    /// <summary>RegisterMyPushDeviceCommand/RevokeMyPushDeviceCommand don't implement IScopedRequest, so
    /// AuthorizationBehaviour never calls this - it only needs to exist so the behaviour's ctor resolves.</summary>
    private sealed class UnrestrictedDataScope : IDataScopeService
    {
        public Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>?>(null);

        public Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<IReadOnlySet<Guid>?> GetAccessibleOrgUnitIdsAsync(string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<Guid>?>(null);
    }

    private sealed class AllAccessSettingsEnabled : IAccessSettingsService
    {
        public Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> IsEnabledForCurrentUserAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
