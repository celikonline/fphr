using NSubstitute;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.CompanyEvents;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

/// <summary>
/// Company calendar merge (senseik-027 acceptance: "CRUD entegrasyon testi" - this codebase's Announcements test
/// project favors handler-level tests with NSubstitute ports over Testcontainers, see AnnouncementQueriesTests).
/// Covers both derivation feeds required by the card: birthdays and leave starts.
/// </summary>
public sealed class CompanyEventQueriesTests
{
    private static (
        ITenantContext Tenant, ICurrentUser CurrentUser, ICompanyEventRepository Events, IEmployeeDirectory Directory,
        IEmployeeContactLookup Contacts, IEmployeeBirthDateLookup BirthDates, IEmployeeLookup EmployeeLookup, ILeaveStartDateReader LeaveStarts)
        BuildSubstitutes(Guid tenantId)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        var events = Substitute.For<ICompanyEventRepository>();
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.ListActiveEmployeeIdsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        var contacts = Substitute.For<IEmployeeContactLookup>();
        var birthDates = Substitute.For<IEmployeeBirthDateLookup>();
        var employeeLookup = Substitute.For<IEmployeeLookup>();
        var leaveStarts = Substitute.For<ILeaveStartDateReader>();
        leaveStarts.GetApprovedLeaveStartsAsync(tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<LeaveStartDate>());
        events.GetInRangeAsync(tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<CompanyEvent>());

        return (tenant, currentUser, events, directory, contacts, birthDates, employeeLookup, leaveStarts);
    }

    [Fact]
    public async Task Handle_WithInvalidRange_ReturnsValidationError()
    {
        var tenantId = Guid.NewGuid();
        var s = BuildSubstitutes(tenantId);
        var handler = new GetCompanyCalendarHandler(s.Tenant, s.CurrentUser, s.Events, s.Directory, s.Contacts, s.BirthDates, s.EmployeeLookup, s.LeaveStarts, Substitute.For<Sense.Modules.Identity.Contracts.ITenantTimeZoneLookup>());

        var result = await handler.Handle(new GetCompanyCalendarQuery(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 1), null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnouncementsErrors.CalendarInvalidRange, result.Error.Code);
    }

    [Fact]
    public async Task Handle_MergesManualEvents_Birthdays_AndLeaveStarts()
    {
        var tenantId = Guid.NewGuid();
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        var s = BuildSubstitutes(tenantId);

        var manual = CompanyEvent.Create(tenantId, "Yıl Sonu Kutlaması", null, new DateOnly(2026, 9, 10), CompanyEventType.CompanyEvent, null, Guid.NewGuid()).Value;
        s.Events.GetInRangeAsync(tenantId, from, to, Arg.Any<CancellationToken>()).Returns(new List<CompanyEvent> { manual });

        var birthdayEmployeeId = Guid.NewGuid();
        s.Directory.ListActiveEmployeeIdsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { birthdayEmployeeId });
        s.BirthDates.GetBirthDatesAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, DateOnly> { [birthdayEmployeeId] = new(1990, 9, 15) });

        var leaveEmployeeId = Guid.NewGuid();
        s.LeaveStarts.GetApprovedLeaveStartsAsync(tenantId, from, to, Arg.Any<CancellationToken>())
            .Returns(new List<LeaveStartDate> { new(leaveEmployeeId, new DateOnly(2026, 9, 20)) });

        s.Contacts.GetContactsAsync(tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ids = callInfo.Arg<IReadOnlyCollection<Guid>>();
                return ids.Select(id => new EmployeeContact(id, "Ada", "Yılmaz", null, null, null, "Active")).ToList();
            });

        var handler = new GetCompanyCalendarHandler(s.Tenant, s.CurrentUser, s.Events, s.Directory, s.Contacts, s.BirthDates, s.EmployeeLookup, s.LeaveStarts, Substitute.For<Sense.Modules.Identity.Contracts.ITenantTimeZoneLookup>());
        var result = await handler.Handle(new GetCompanyCalendarQuery(from, to, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Contains(result.Value, e => e.Type == CompanyEventType.CompanyEvent && e.Id == manual.Id);
        Assert.Contains(result.Value, e => e.Type == CompanyEventType.Birthday && e.EmployeeId == birthdayEmployeeId && e.Date == new DateOnly(2026, 9, 15));
        Assert.Contains(result.Value, e => e.Type == CompanyEventType.LeaveStart && e.EmployeeId == leaveEmployeeId && e.Date == new DateOnly(2026, 9, 20));
    }

    [Fact]
    public async Task Handle_ExcludesManualEventRestrictedToAnotherOrgUnit()
    {
        var tenantId = Guid.NewGuid();
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        var callerOrgUnitId = Guid.NewGuid();
        var otherOrgUnitId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var s = BuildSubstitutes(tenantId);
        s.CurrentUser.EmployeeId.Returns(employeeId);
        s.EmployeeLookup.GetOrgUnitIdAsync(tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(callerOrgUnitId);

        var restricted = CompanyEvent.Create(tenantId, "Departman Toplantısı", null, new DateOnly(2026, 9, 12), CompanyEventType.Meeting, otherOrgUnitId, Guid.NewGuid()).Value;
        var visible = CompanyEvent.Create(tenantId, "Ekip Etkinliği", null, new DateOnly(2026, 9, 13), CompanyEventType.CompanyEvent, callerOrgUnitId, Guid.NewGuid()).Value;
        s.Events.GetInRangeAsync(tenantId, from, to, Arg.Any<CancellationToken>()).Returns(new List<CompanyEvent> { restricted, visible });

        var handler = new GetCompanyCalendarHandler(s.Tenant, s.CurrentUser, s.Events, s.Directory, s.Contacts, s.BirthDates, s.EmployeeLookup, s.LeaveStarts, Substitute.For<Sense.Modules.Identity.Contracts.ITenantTimeZoneLookup>());
        var result = await handler.Handle(new GetCompanyCalendarQuery(from, to, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(result.Value);
        Assert.Equal(visible.Id, entry.Id);
    }

    [Fact]
    public void Query_RequiresMyEventsPermission()
    {
        var permissions = typeof(GetCompanyCalendarQuery)
            .GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: true)
            .Cast<RequiresPermissionAttribute>()
            .Select(a => a.Permission)
            .ToList();

        Assert.Contains("announcements.my.events", permissions);
    }
}
