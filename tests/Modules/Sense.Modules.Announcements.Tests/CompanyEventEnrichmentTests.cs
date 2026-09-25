using NSubstitute;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.CompanyEvents;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Xunit;

namespace Sense.Modules.Announcements.Tests;

/// <summary>E4 etkinlik zenginlestirme: zaman araligi, tum gun, yuz yuze/online, kategori, davetliler.</summary>
public sealed class CompanyEventEnrichmentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 10, 5, 10, 0, 0);

    private static CompanyEventPlan Plan(
        DateTime? ends = null, CompanyEventMode? mode = null, string? location = null, string? url = null, bool notify = false) =>
        new(Start, ends, false, mode, location, url, notify);

    [Fact]
    public void Legacy_event_keeps_date_as_all_day()
    {
        var created = CompanyEvent.Create(TenantId, "Eski", null, new DateOnly(2026, 9, 10), CompanyEventType.CompanyEvent, null, Guid.NewGuid());

        Assert.True(created.IsSuccess);
        Assert.True(created.Value.IsAllDay);
        Assert.Equal(new DateOnly(2026, 9, 10), created.Value.EventDate);
        Assert.Equal(new DateTime(2026, 9, 10), created.Value.StartsAt);
        Assert.Null(created.Value.Mode);
    }

    [Fact]
    public void EventDate_is_derived_from_StartsAt()
    {
        var created = CompanyEvent.Create(TenantId, "Toplanti", null, CompanyEventType.Meeting, null, Guid.NewGuid(), Plan());

        Assert.Equal(new DateOnly(2026, 10, 5), created.Value.EventDate);
    }

    [Fact]
    public void Online_requires_url_onsite_requires_location()
    {
        var onlineNoUrl = CompanyEvent.Create(TenantId, "T", null, CompanyEventType.Meeting, null, Guid.NewGuid(), Plan(mode: CompanyEventMode.Online));
        var onlineBadUrl = CompanyEvent.Create(TenantId, "T", null, CompanyEventType.Meeting, null, Guid.NewGuid(), Plan(mode: CompanyEventMode.Online, url: "ftp://x"));
        var onsiteNoLocation = CompanyEvent.Create(TenantId, "T", null, CompanyEventType.Meeting, null, Guid.NewGuid(), Plan(mode: CompanyEventMode.Onsite));
        var online = CompanyEvent.Create(TenantId, "T", null, CompanyEventType.Meeting, null, Guid.NewGuid(), Plan(mode: CompanyEventMode.Online, url: "https://meet.example.com/a", location: "ignored"));
        var onsite = CompanyEvent.Create(TenantId, "T", null, CompanyEventType.Meeting, null, Guid.NewGuid(), Plan(mode: CompanyEventMode.Onsite, location: "Salon 1", url: "https://ignored.example.com"));

        Assert.Equal(CompanyEventDomainErrors.OnlineUrlRequired, onlineNoUrl.Error.Code);
        Assert.Equal(CompanyEventDomainErrors.OnlineUrlRequired, onlineBadUrl.Error.Code);
        Assert.Equal(CompanyEventDomainErrors.LocationRequired, onsiteNoLocation.Error.Code);
        Assert.True(online.IsSuccess);
        Assert.Null(online.Value.Location);
        Assert.True(onsite.IsSuccess);
        Assert.Null(onsite.Value.OnlineUrl);
    }

    [Fact]
    public void EndsAt_must_not_precede_StartsAt()
    {
        var result = CompanyEvent.Create(TenantId, "T", null, CompanyEventType.Meeting, null, Guid.NewGuid(), Plan(ends: Start.AddMinutes(-1)));

        Assert.Equal(CompanyEventDomainErrors.EndsBeforeStarts, result.Error.Code);
    }

    [Fact]
    public void SetInvitees_returns_only_newly_added_and_drops_removed()
    {
        var companyEvent = CompanyEvent.Create(TenantId, "T", null, CompanyEventType.Meeting, null, Guid.NewGuid(), Plan()).Value;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        companyEvent.SetInvitees([a, b, a]);
        var added = companyEvent.SetInvitees([b, c]);

        Assert.Equal([c], added);
        Assert.Equal(new[] { b, c }.Order(), companyEvent.Invitees.Select(i => i.EmployeeId).Order());
    }

    [Theory]
    [InlineData("#12abEF", true)]
    [InlineData("red", false)]
    [InlineData("#12345", false)]
    public void Category_color_must_be_hex(string color, bool valid) =>
        Assert.Equal(valid, CompanyEventCategory.Create(TenantId, "Kutlama", color).IsSuccess);

    private static (CreateCompanyEventHandler Handler, IIntegrationEventOutbox Outbox, ICompanyEventRepository Events) CreateHandler(
        IReadOnlyList<Guid> knownEmployees)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var currentUser = Substitute.For<ICurrentUser>();
        var events = Substitute.For<ICompanyEventRepository>();
        var contacts = Substitute.For<IEmployeeContactLookup>();
        contacts.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IReadOnlyCollection<Guid>)ci[1]).Where(knownEmployees.Contains)
                .Select(id => new EmployeeContact(id, "Ad", "Soyad", null, null, null, "Active")).ToList());
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var handler = new CreateCompanyEventHandler(
            tenant, currentUser, events, Substitute.For<IOrgUnitLookup>(), Substitute.For<ICompanyEventCategoryRepository>(), contacts, outbox);
        return (handler, outbox, events);
    }

    private static CreateCompanyEventCommand Command(bool notify, IReadOnlyList<Guid> invitees) =>
        new("Kutlama", null, new DateOnly(2026, 10, 5), CompanyEventType.CompanyEvent, null,
            StartsAt: Start, EndsAt: Start.AddHours(2), IsAllDay: false, Mode: CompanyEventMode.Onsite, Location: "Ofis",
            NotifyInvitees: notify, InviteeEmployeeIds: invitees);

    [Fact]
    public async Task Invitees_notified_when_flag_set()
    {
        var invitees = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var (handler, outbox, events) = CreateHandler(invitees);

        var result = await handler.Handle(Command(notify: true, invitees), CancellationToken.None);

        Assert.True(result.IsSuccess);
        events.Received(1).Add(Arg.Is<CompanyEvent>(e => e.Invitees.Count == 2));
        outbox.Received(1).Enqueue(Arg.Is<IIntegrationEvent>(e =>
            e is CompanyEventInvitationRequested && ((CompanyEventInvitationRequested)e).EmployeeIds.Count == 2
            && ((CompanyEventInvitationRequested)e).Location == "Ofis"));
    }

    [Fact]
    public async Task Invitees_not_notified_when_flag_unset()
    {
        var invitees = new[] { Guid.NewGuid() };
        var (handler, outbox, _) = CreateHandler(invitees);

        var result = await handler.Handle(Command(notify: false, invitees), CancellationToken.None);

        Assert.True(result.IsSuccess);
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
    }

    [Fact]
    public async Task Unknown_invitee_is_rejected()
    {
        var (handler, outbox, events) = CreateHandler([]);

        var result = await handler.Handle(Command(notify: true, [Guid.NewGuid()]), CancellationToken.None);

        Assert.Equal(CompanyEventDomainErrors.InviteeNotFound, result.Error.Code);
        events.DidNotReceive().Add(Arg.Any<CompanyEvent>());
        outbox.DidNotReceive().Enqueue(Arg.Any<IIntegrationEvent>());
    }

    [Fact]
    public async Task Calendar_returns_events_overlapping_range()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var events = Substitute.For<ICompanyEventRepository>();
        var multiDay = CompanyEvent.Create(TenantId, "Konferans", null, CompanyEventType.CompanyEvent, null, Guid.NewGuid(),
            new CompanyEventPlan(new DateTime(2026, 8, 30), new DateTime(2026, 9, 2), true)).Value;
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        events.GetInRangeAsync(TenantId, from, to, Arg.Any<CancellationToken>()).Returns(new List<CompanyEvent> { multiDay });
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.ListActiveEmployeeIdsAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        var leaveStarts = Substitute.For<ILeaveStartDateReader>();
        leaveStarts.GetApprovedLeaveStartsAsync(TenantId, from, to, Arg.Any<CancellationToken>()).Returns(new List<LeaveStartDate>());
        var handler = new GetCompanyCalendarHandler(
            tenant, Substitute.For<ICurrentUser>(), events, directory, Substitute.For<IEmployeeContactLookup>(),
            Substitute.For<IEmployeeBirthDateLookup>(), Substitute.For<IEmployeeLookup>(), leaveStarts);

        var result = await handler.Handle(new GetCompanyCalendarQuery(from, to, null), CancellationToken.None);

        var entry = Assert.Single(result.Value);
        Assert.Equal("Konferans", entry.Title);
        Assert.Equal(new DateTime(2026, 9, 2), entry.EndsAt);
        Assert.True(entry.IsAllDay);
    }
}
