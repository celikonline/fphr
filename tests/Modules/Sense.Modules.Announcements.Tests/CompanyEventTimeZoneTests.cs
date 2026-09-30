using NSubstitute;
using Sense.Modules.Announcements.Application.CompanyEvents;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

/// <summary>B14: timed events derive their calendar day from the tenant local day, not the UTC day.</summary>
public sealed class CompanyEventTimeZoneTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ITenantTimeZoneLookup Istanbul()
    {
        var lookup = Substitute.For<ITenantTimeZoneLookup>();
        lookup.FindTimeZoneIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns("Europe/Istanbul");
        return lookup;
    }

    private static async Task<CompanyEvent> CreateAsync(CreateCompanyEventCommand command)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var events = Substitute.For<ICompanyEventRepository>();
        var handler = new CreateCompanyEventHandler(
            tenant, Substitute.For<ICurrentUser>(), events, Substitute.For<IOrgUnitLookup>(),
            Substitute.For<ICompanyEventCategoryRepository>(), Substitute.For<IEmployeeContactLookup>(),
            Substitute.For<IIntegrationEventOutbox>(), Istanbul());

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        return (CompanyEvent)events.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "Add").GetArguments()[0]!;
    }

    [Fact]
    public async Task Timed_event_at_local_00_30_uses_local_day()
    {
        var utc = new DateTime(2026, 9, 10, 21, 30, 0, DateTimeKind.Utc); // 2026-09-11 00:30 in UTC+3

        var created = await CreateAsync(new CreateCompanyEventCommand(
            "Gece", null, new DateOnly(2026, 9, 10), CompanyEventType.Meeting, null, StartsAt: utc, IsAllDay: false));

        Assert.Equal(new DateOnly(2026, 9, 11), created.EventDate);
        Assert.Equal(utc, created.StartsAt);
    }

    [Fact]
    public async Task All_day_event_unchanged()
    {
        var created = await CreateAsync(new CreateCompanyEventCommand(
            "Gun", null, new DateOnly(2026, 9, 10), CompanyEventType.CompanyEvent, null));

        Assert.Equal(new DateOnly(2026, 9, 10), created.EventDate);
    }

    [Fact]
    public async Task Calendar_range_uses_tenant_local_day()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var late = CompanyEvent.Create(TenantId, "Gece", null, CompanyEventType.Meeting, null, Guid.NewGuid(),
            new CompanyEventPlan(new DateTime(2026, 8, 31, 21, 30, 0), null, false, LocalStartDate: new DateOnly(2026, 9, 1))).Value;
        // Local 2026-09-30 22:00 -> UTC 19:00 same day: inside; local 2026-10-01 01:00 -> UTC 09-30 22:00: outside range.
        var outside = CompanyEvent.Create(TenantId, "Disarda", null, CompanyEventType.Meeting, null, Guid.NewGuid(),
            new CompanyEventPlan(new DateTime(2026, 9, 30, 22, 0, 0), null, false, LocalStartDate: new DateOnly(2026, 10, 1))).Value;
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        var events = Substitute.For<ICompanyEventRepository>();
        events.GetInRangeAsync(TenantId, from.AddDays(-1), to.AddDays(1), Arg.Any<CancellationToken>()).Returns(new List<CompanyEvent> { late, outside });
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.ListActiveEmployeeIdsAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        var leaveStarts = Substitute.For<ILeaveStartDateReader>();
        leaveStarts.GetApprovedLeaveStartsAsync(TenantId, from, to, Arg.Any<CancellationToken>()).Returns(new List<LeaveStartDate>());
        var handler = new GetCompanyCalendarHandler(
            tenant, Substitute.For<ICurrentUser>(), events, directory, Substitute.For<IEmployeeContactLookup>(),
            Substitute.For<IEmployeeBirthDateLookup>(), Substitute.For<IEmployeeLookup>(), leaveStarts, Istanbul());

        var result = await handler.Handle(new GetCompanyCalendarQuery(from, to, null), CancellationToken.None);

        var entry = Assert.Single(result.Value);
        Assert.Equal("Gece", entry.Title);
        Assert.Equal(new DateOnly(2026, 9, 1), entry.Date);
    }
}
