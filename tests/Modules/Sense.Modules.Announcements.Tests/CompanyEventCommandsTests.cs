using NSubstitute;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.CompanyEvents;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

public sealed class CompanyEventCommandsTests
{
    [Fact]
    public async Task CreateCompanyEvent_WithValidCommand_ShouldSucceed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new CreateCompanyEventCommand("Yıl Sonu Kutlaması", "Ofiste kutlama", new DateOnly(2026, 12, 20), CompanyEventType.CompanyEvent, null);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        var repository = Substitute.For<ICompanyEventRepository>();
        var orgUnits = Substitute.For<IOrgUnitLookup>();

        var handler = new CreateCompanyEventHandler(tenant, currentUser, repository, orgUnits);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
        repository.Received(1).Add(Arg.Is<CompanyEvent>(e => e.TenantId == tenantId && e.Title == "Yıl Sonu Kutlaması" && e.CreatedByUserId == userId));
    }

    [Fact]
    public async Task CreateCompanyEvent_WithUnknownVisibilityOrgUnit_Fails()
    {
        var tenantId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var command = new CreateCompanyEventCommand("Sprint Planlama", null, new DateOnly(2026, 9, 22), CompanyEventType.Meeting, orgUnitId);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var repository = Substitute.For<ICompanyEventRepository>();
        var orgUnits = Substitute.For<IOrgUnitLookup>();
        orgUnits.ExistsActiveAsync(tenantId, orgUnitId, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new CreateCompanyEventHandler(tenant, currentUser, repository, orgUnits);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnouncementsErrors.VisibilityOrgUnitNotFound, result.Error.Code);
        repository.DidNotReceive().Add(Arg.Any<CompanyEvent>());
    }

    [Fact]
    public void CreateCompanyEventValidator_WithEmptyTitle_ShouldFail()
    {
        var command = new CreateCompanyEventCommand("", null, new DateOnly(2026, 9, 22), CompanyEventType.Meeting, null);
        var result = new CreateCompanyEventValidator().Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateCompanyEventValidator_WithBirthdayType_ShouldFail()
    {
        // Birthday/LeaveStart are derived-only, never manually created (CompanyEvent.Create also rejects this).
        var command = new CreateCompanyEventCommand("Doğum günü", null, new DateOnly(2026, 9, 22), CompanyEventType.Birthday, null);
        var result = new CreateCompanyEventValidator().Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task DeleteCompanyEvent_WhenNotFound_Fails()
    {
        var tenantId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var repository = Substitute.For<ICompanyEventRepository>();
        repository.GetByIdAsync(tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CompanyEvent?)null);

        var handler = new DeleteCompanyEventHandler(tenant, repository);
        var result = await handler.Handle(new DeleteCompanyEventCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(CompanyEventDomainErrors.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task DeleteCompanyEvent_WhenFound_RemovesIt()
    {
        var tenantId = Guid.NewGuid();
        var companyEvent = CompanyEvent.Create(tenantId, "Toplantı", null, new DateOnly(2026, 9, 22), CompanyEventType.Meeting, null, Guid.NewGuid()).Value;

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var repository = Substitute.For<ICompanyEventRepository>();
        repository.GetByIdAsync(tenantId, companyEvent.Id, Arg.Any<CancellationToken>()).Returns(companyEvent);

        var handler = new DeleteCompanyEventHandler(tenant, repository);
        var result = await handler.Handle(new DeleteCompanyEventCommand(companyEvent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.Received(1).Remove(companyEvent);
    }

    [Fact]
    public async Task UpdateCompanyEvent_WhenNotFound_Fails()
    {
        var tenantId = Guid.NewGuid();
        var command = new UpdateCompanyEventCommand(Guid.NewGuid(), "Yeni başlık", null, new DateOnly(2026, 9, 22), CompanyEventType.Meeting, null);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var repository = Substitute.For<ICompanyEventRepository>();
        repository.GetByIdAsync(tenantId, command.EventId, Arg.Any<CancellationToken>()).Returns((CompanyEvent?)null);
        var orgUnits = Substitute.For<IOrgUnitLookup>();

        var handler = new UpdateCompanyEventHandler(tenant, repository, orgUnits);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(CompanyEventDomainErrors.NotFound, result.Error.Code);
    }

    [Fact]
    public async Task UpdateCompanyEvent_WithValidCommand_UpdatesFieldsInPlace()
    {
        var tenantId = Guid.NewGuid();
        var companyEvent = CompanyEvent.Create(
            tenantId, "Eski başlık", "Eski açıklama", new DateOnly(2026, 9, 22), CompanyEventType.Meeting, null, Guid.NewGuid()).Value;
        var originalId = companyEvent.Id;
        var command = new UpdateCompanyEventCommand(
            originalId, "Yeni başlık", "Yeni açıklama", new DateOnly(2026, 10, 1), CompanyEventType.CompanyEvent, null);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var repository = Substitute.For<ICompanyEventRepository>();
        repository.GetByIdAsync(tenantId, originalId, Arg.Any<CancellationToken>()).Returns(companyEvent);
        var orgUnits = Substitute.For<IOrgUnitLookup>();

        var handler = new UpdateCompanyEventHandler(tenant, repository, orgUnits);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(originalId, companyEvent.Id);
        Assert.Equal("Yeni başlık", companyEvent.Title);
        Assert.Equal("Yeni açıklama", companyEvent.Description);
        Assert.Equal(new DateOnly(2026, 10, 1), companyEvent.EventDate);
        Assert.Equal(CompanyEventType.CompanyEvent, companyEvent.Type);
        repository.DidNotReceive().Remove(Arg.Any<CompanyEvent>());
        repository.DidNotReceive().Add(Arg.Any<CompanyEvent>());
    }

    [Fact]
    public async Task UpdateCompanyEvent_WithUnknownVisibilityOrgUnit_Fails()
    {
        var tenantId = Guid.NewGuid();
        var orgUnitId = Guid.NewGuid();
        var companyEvent = CompanyEvent.Create(
            tenantId, "Toplantı", null, new DateOnly(2026, 9, 22), CompanyEventType.Meeting, null, Guid.NewGuid()).Value;
        var command = new UpdateCompanyEventCommand(
            companyEvent.Id, "Toplantı", null, new DateOnly(2026, 9, 22), CompanyEventType.Meeting, orgUnitId);

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var repository = Substitute.For<ICompanyEventRepository>();
        repository.GetByIdAsync(tenantId, companyEvent.Id, Arg.Any<CancellationToken>()).Returns(companyEvent);
        var orgUnits = Substitute.For<IOrgUnitLookup>();
        orgUnits.ExistsActiveAsync(tenantId, orgUnitId, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new UpdateCompanyEventHandler(tenant, repository, orgUnits);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnouncementsErrors.VisibilityOrgUnitNotFound, result.Error.Code);
        Assert.Equal("Toplantı", companyEvent.Title);
    }

    [Fact]
    public void UpdateCompanyEventValidator_WithEmptyTitle_ShouldFail()
    {
        var command = new UpdateCompanyEventCommand(Guid.NewGuid(), "", null, new DateOnly(2026, 9, 22), CompanyEventType.Meeting, null);
        var result = new UpdateCompanyEventValidator().Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Commands_RequireEventsManagePermission()
    {
        var createPermissions = typeof(CreateCompanyEventCommand)
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: true)
            .Cast<RequiresPermissionAttribute>()
            .Select(a => a.Permission)
            .ToList();
        var updatePermissions = typeof(UpdateCompanyEventCommand)
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: true)
            .Cast<RequiresPermissionAttribute>()
            .Select(a => a.Permission)
            .ToList();
        var deletePermissions = typeof(DeleteCompanyEventCommand)
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: true)
            .Cast<RequiresPermissionAttribute>()
            .Select(a => a.Permission)
            .ToList();

        Assert.Contains("announcements.events.manage", createPermissions);
        Assert.Contains("announcements.events.manage", updatePermissions);
        Assert.Contains("announcements.events.manage", deletePermissions);
    }
}
