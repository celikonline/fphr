using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.OccupationalSafety.Application.Incidents;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Domain.Incidents;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Xunit;

namespace Sense.Modules.OccupationalSafety.Tests;

public class SgkNotificationDeadlineTests
{
    private static Task<bool> NoHoliday(DateOnly _) => Task.FromResult(false);

    [Theory]
    [InlineData("2026-09-21", "2026-09-24")] // Monday → Thursday
    [InlineData("2026-09-24", "2026-09-29")] // Thursday → Fri, Mon, Tue
    [InlineData("2026-09-26", "2026-09-30")] // Saturday → Mon, Tue, Wed
    public async Task Deadline_IsThirdBusinessDayAfterTheAccident(string eventDate, string expected)
    {
        var deadline = await SgkNotificationDeadline.CalculateAsync(DateOnly.Parse(eventDate), NoHoliday);

        Assert.Equal(DateOnly.Parse(expected), deadline);
    }

    [Fact]
    public async Task Deadline_SkipsHolidays()
    {
        var holiday = new DateOnly(2026, 10, 29); // Cumhuriyet Bayramı, Thursday
        var deadline = await SgkNotificationDeadline.CalculateAsync(
            new DateOnly(2026, 10, 27), day => Task.FromResult(day == holiday));

        Assert.Equal(new DateOnly(2026, 11, 2), deadline); // Wed, (Thu holiday), Fri, Mon
    }
}

public class OhsIncidentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 9, 22);

    private static OhsIncident Accident() =>
        OhsIncident.Create(TenantId, OhsIncidentKind.WorkAccident, Today.AddDays(-1), Today, null, Guid.NewGuid(), null,
            "Merdivenden düştü", Today.AddDays(2)).Value;

    [Fact]
    public void WorkAccident_WithoutInjuredEmployee_IsRejected()
    {
        var result = OhsIncident.Create(TenantId, OhsIncidentKind.WorkAccident, Today, Today, null, null, null, "Kaza", Today.AddDays(3));

        Assert.Equal(OhsDomainErrors.IncidentEmployeeRequired, result.Error.Code);
    }

    [Fact]
    public void Incident_InTheFuture_IsRejected()
    {
        var result = OhsIncident.Create(TenantId, OhsIncidentKind.NearMiss, Today.AddDays(1), Today, null, null, null, "Kayma", null);

        Assert.Equal(OhsDomainErrors.IncidentDateInFuture, result.Error.Code);
    }

    [Fact]
    public void NearMiss_HasNoSgkDeadline_AndCannotBeMarkedNotified()
    {
        var nearMiss = OhsIncident.Create(TenantId, OhsIncidentKind.NearMiss, Today, Today, null, null, null, "Kayma", Today).Value;

        Assert.Null(nearMiss.SgkNotificationDeadline);
        Assert.Equal(OhsDomainErrors.IncidentSgkNotApplicable, nearMiss.MarkSgkNotified(Today, Today).Error.Code);
    }

    [Fact]
    public void WorkAccident_CannotCloseBeforeSgkNotification()
    {
        var accident = Accident();

        Assert.Equal(OhsDomainErrors.IncidentSgkNotNotified, accident.Close("Korkuluk takıldı", Today).Error.Code);

        Assert.True(accident.MarkSgkNotified(Today, Today).IsSuccess);
        Assert.True(accident.Close("Korkuluk takıldı", Today).IsSuccess);
        Assert.True(accident.IsClosed);
        Assert.Equal(OhsDomainErrors.IncidentAlreadyClosed, accident.Close("Tekrar", Today).Error.Code);
    }

    [Fact]
    public void SgkNotification_BeforeTheAccident_IsRejected()
    {
        var accident = Accident();

        Assert.Equal(OhsDomainErrors.IncidentSgkDateInvalid, accident.MarkSgkNotified(Today.AddDays(-5), Today).Error.Code);
    }
}

public class IncidentHandlerTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IOhsIncidentRepository _incidents = Substitute.For<IOhsIncidentRepository>();
    private readonly TimeProvider _clock = new FixedClock(new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero));

    public IncidentHandlerTests() => _tenant.TenantId.Returns(_tenantId);

    [Fact]
    public async Task RecordWorkAccident_ComputesSgkDeadlineFromTheHolidayCalendar()
    {
        var employeeId = Guid.NewGuid();
        var employees = Substitute.For<IEmployeeLookup>();
        employees.ExistsActiveAsync(_tenantId, employeeId, Arg.Any<CancellationToken>()).Returns(true);
        var holidays = Substitute.For<IHolidayCalendarLookup>();
        holidays.IsHolidayAsync(_tenantId, new DateOnly(2026, 9, 25), Arg.Any<CancellationToken>()).Returns(true);
        OhsIncident? added = null;
        _incidents.Add(Arg.Do<OhsIncident>(i => added = i));

        var result = await new RecordIncidentHandler(_tenant, _incidents, employees, holidays, _clock).Handle(
            new RecordIncidentCommand(OhsIncidentKind.WorkAccident, new DateOnly(2026, 9, 24), null, employeeId, "Elini kesti"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Thursday accident, Friday holiday → Mon, Tue, Wed.
        Assert.Equal(new DateOnly(2026, 9, 30), added!.SgkNotificationDeadline);
    }

    [Fact]
    public async Task RecordIncident_ForUnknownEmployee_IsRejected()
    {
        var employees = Substitute.For<IEmployeeLookup>();
        var holidays = Substitute.For<IHolidayCalendarLookup>();

        var result = await new RecordIncidentHandler(_tenant, _incidents, employees, holidays, _clock).Handle(
            new RecordIncidentCommand(OhsIncidentKind.WorkAccident, new DateOnly(2026, 9, 24), null, Guid.NewGuid(), "Kaza"),
            CancellationToken.None);

        Assert.Equal(OhsDomainErrors.IncidentEmployeeNotFound, result.Error.Code);
        _incidents.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task ReportNearMiss_RecordsTheCallerAsReporter()
    {
        var reporterId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(reporterId);
        OhsIncident? added = null;
        _incidents.Add(Arg.Do<OhsIncident>(i => added = i));

        var result = await new ReportNearMissHandler(_tenant, currentUser, _incidents, _clock).Handle(
            new ReportNearMissCommand(new DateOnly(2026, 9, 25), null, "Islak zemin, uyarı levhası yok"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OhsIncidentKind.NearMiss, added!.Kind);
        Assert.Equal(reporterId, added.ReportedByEmployeeId);
        Assert.Null(added.EmployeeId);
    }

    [Fact]
    public async Task ListIncidents_FlagsUnnotifiedAccidentsPastTheirDeadline()
    {
        var overdue = OhsIncident.Create(_tenantId, OhsIncidentKind.WorkAccident, new DateOnly(2026, 9, 18),
            new DateOnly(2026, 9, 18), null, Guid.NewGuid(), null, "Kaza", new DateOnly(2026, 9, 23)).Value;
        _incidents.ListAsync(Arg.Any<CancellationToken>()).Returns([overdue]);

        var result = await new ListIncidentsHandler(_incidents, _clock).Handle(new ListIncidentsQuery(), CancellationToken.None);

        Assert.True(Assert.Single(result.Value).IsSgkOverdue);
    }
}
