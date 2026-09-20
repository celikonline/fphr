using NSubstitute;
using Sense.Modules.Organization.Application;
using Sense.Modules.Organization.Application.HolidayCalendars;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Handler tests for holiday calendar management (audit A3): rename/delete a calendar, edit/delete an entry.
/// The system "Türkiye Resmi Tatilleri" calendar is read-only for these operations and a calendar assigned to a
/// company cannot be deleted. Fake-repo style (NSubstitute), same as CostCenterHandlerTests.
/// </summary>
public sealed class HolidayCalendarManagementHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static HolidayCalendar Custom(string name = "QA Takvim") =>
        HolidayCalendar.Create(TenantId, name, "TR", isSystemDefined: false).Value;

    private static HolidayCalendar System() =>
        HolidayCalendar.Create(TenantId, "Türkiye Resmi Tatilleri", "TR", isSystemDefined: true).Value;

    private static IHolidayCalendarRepository CalendarsReturning(HolidayCalendar calendar)
    {
        var repo = Substitute.For<IHolidayCalendarRepository>();
        repo.GetByIdAsync(calendar.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(calendar)));
        return repo;
    }

    private static HolidayCalendarEntry NewEntry(Guid calendarId) =>
        HolidayCalendarEntry.Create(TenantId, calendarId, new DateOnly(2026, 11, 15), "QA Özel Tatil", isRecurringAnnually: false).Value;

    [Fact]
    public async Task Rename_CustomCalendar_UpdatesName()
    {
        var calendar = Custom();
        var handler = new UpdateHolidayCalendarHandler(CalendarsReturning(calendar));

        var result = await handler.Handle(new UpdateHolidayCalendarCommand(calendar.Id, "Yeni ad"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Yeni ad");
        calendar.Name.ShouldBe("Yeni ad");
    }

    [Fact]
    public async Task Rename_SystemCalendar_Fails()
    {
        var calendar = System();
        var handler = new UpdateHolidayCalendarHandler(CalendarsReturning(calendar));

        var result = await handler.Handle(new UpdateHolidayCalendarCommand(calendar.Id, "Başka ad"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarSystemImmutable);
    }

    [Fact]
    public async Task Rename_UnknownCalendar_ReturnsNotFound()
    {
        var repo = Substitute.For<IHolidayCalendarRepository>();
        repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Result<HolidayCalendar>>(Error.NotFound(OrganizationDomainErrors.HolidayCalendarNotFound)));
        var handler = new UpdateHolidayCalendarHandler(repo);

        var result = await handler.Handle(new UpdateHolidayCalendarCommand(Guid.NewGuid(), "x"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationErrors.HolidayCalendarNotFound);
    }

    [Fact]
    public async Task Delete_UnassignedCustomCalendar_RemovesIt()
    {
        var calendar = Custom();
        var repo = CalendarsReturning(calendar);
        repo.IsAssignedToCompanyAsync(calendar.Id, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new DeleteHolidayCalendarHandler(repo);

        var result = await handler.Handle(new DeleteHolidayCalendarCommand(calendar.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.Received(1).Remove(calendar);
    }

    [Fact]
    public async Task Delete_SystemCalendar_Fails_AndDoesNotRemove()
    {
        var calendar = System();
        var repo = CalendarsReturning(calendar);
        var handler = new DeleteHolidayCalendarHandler(repo);

        var result = await handler.Handle(new DeleteHolidayCalendarCommand(calendar.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarSystemImmutable);
        repo.DidNotReceive().Remove(Arg.Any<HolidayCalendar>());
    }

    [Fact]
    public async Task Delete_CalendarAssignedToACompany_FailsWithConflict()
    {
        var calendar = Custom();
        var repo = CalendarsReturning(calendar);
        repo.IsAssignedToCompanyAsync(calendar.Id, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DeleteHolidayCalendarHandler(repo);

        var result = await handler.Handle(new DeleteHolidayCalendarCommand(calendar.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationErrors.HolidayCalendarInUse);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        repo.DidNotReceive().Remove(Arg.Any<HolidayCalendar>());
    }

    [Fact]
    public async Task UpdateEntry_CustomCalendar_ChangesEntry_IncludingHalfDay()
    {
        var calendar = Custom();
        var entry = NewEntry(calendar.Id);
        var entries = Substitute.For<IHolidayCalendarEntryRepository>();
        entries.FindAsync(calendar.Id, entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var handler = new UpdateHolidayCalendarEntryHandler(CalendarsReturning(calendar), entries);

        var result = await handler.Handle(
            new UpdateHolidayCalendarEntryCommand(calendar.Id, entry.Id, new DateOnly(2026, 11, 16), "Yarım gün", false, true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Date.ShouldBe(new DateOnly(2026, 11, 16));
        result.Value.Name.ShouldBe("Yarım gün");
        result.Value.IsHalfDay.ShouldBeTrue();
        entry.IsHalfDay.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateEntry_SystemCalendar_Fails()
    {
        var calendar = System();
        var entry = NewEntry(calendar.Id);
        var entries = Substitute.For<IHolidayCalendarEntryRepository>();
        entries.FindAsync(calendar.Id, entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var handler = new UpdateHolidayCalendarEntryHandler(CalendarsReturning(calendar), entries);

        var result = await handler.Handle(
            new UpdateHolidayCalendarEntryCommand(calendar.Id, entry.Id, entry.Date, "Değişti", false, false), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarSystemImmutable);
        entry.Name.ShouldBe("QA Özel Tatil");
    }

    [Fact]
    public async Task UpdateEntry_EntryOfAnotherCalendar_ReturnsNotFound()
    {
        var calendar = Custom();
        var entries = Substitute.For<IHolidayCalendarEntryRepository>();
        entries.FindAsync(calendar.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HolidayCalendarEntry?)null);
        var handler = new UpdateHolidayCalendarEntryHandler(CalendarsReturning(calendar), entries);

        var result = await handler.Handle(
            new UpdateHolidayCalendarEntryCommand(calendar.Id, Guid.NewGuid(), new DateOnly(2026, 1, 1), "x", false, false), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationErrors.HolidayCalendarEntryNotFound);
    }

    [Fact]
    public async Task DeleteEntry_CustomCalendar_RemovesEntry()
    {
        var calendar = Custom();
        var entry = NewEntry(calendar.Id);
        var entries = Substitute.For<IHolidayCalendarEntryRepository>();
        entries.FindAsync(calendar.Id, entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var handler = new DeleteHolidayCalendarEntryHandler(CalendarsReturning(calendar), entries);

        var result = await handler.Handle(new DeleteHolidayCalendarEntryCommand(calendar.Id, entry.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        entries.Received(1).Remove(entry);
    }

    [Fact]
    public async Task DeleteEntry_SystemCalendar_Fails_AndDoesNotRemove()
    {
        var calendar = System();
        var entry = NewEntry(calendar.Id);
        var entries = Substitute.For<IHolidayCalendarEntryRepository>();
        entries.FindAsync(calendar.Id, entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var handler = new DeleteHolidayCalendarEntryHandler(CalendarsReturning(calendar), entries);

        var result = await handler.Handle(new DeleteHolidayCalendarEntryCommand(calendar.Id, entry.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarSystemImmutable);
        entries.DidNotReceive().Remove(Arg.Any<HolidayCalendarEntry>());
    }

    [Fact]
    public async Task AddEntry_HalfDayFlag_IsPersistedOnTheEntry()
    {
        var calendar = Custom();
        var entries = Substitute.For<IHolidayCalendarEntryRepository>();
        var handler = new AddHolidayCalendarEntryHandler(CalendarsReturning(calendar), entries);

        var result = await handler.Handle(
            new AddHolidayCalendarEntryCommand(calendar.Id, new DateOnly(2026, 10, 28), "Arife", true, true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsHalfDay.ShouldBeTrue();
        entries.Received(1).Add(Arg.Is<HolidayCalendarEntry>(e => e.IsHalfDay));
    }

    [Theory]
    [InlineData("ZZ", false)]
    [InlineData("TR", true)]
    [InlineData("de", true)]
    public async Task Create_ValidatesCountryCodeAgainstIsoList(string countryCode, bool expectSuccess)
    {
        var tenant = Substitute.For<Sense.Shared.Contracts.Context.ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var repo = Substitute.For<IHolidayCalendarRepository>();
        var handler = new CreateHolidayCalendarHandler(tenant, repo);

        var result = await handler.Handle(new CreateHolidayCalendarCommand("QA Takvim", countryCode), CancellationToken.None);

        result.IsSuccess.ShouldBe(expectSuccess);
        if (!expectSuccess)
        {
            result.Error.Code.ShouldBe(OrganizationDomainErrors.HolidayCalendarCountryCodeInvalid);
            repo.DidNotReceive().Add(Arg.Any<HolidayCalendar>());
        }
    }
}
