using NSubstitute;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>Kart senseik-012 §2: 270 saat yıllık fazla mesai sayacı (hesaplanan sorgu, kalıcı entity değil).</summary>
public sealed class OvertimeAnnualUsageTests
{
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly IOvertimeRequestRepository _requests = Substitute.For<IOvertimeRequestRepository>();
    private readonly IOvertimeConsentRepository _consents = Substitute.For<IOvertimeConsentRepository>();
    private readonly IWorkScheduleReader _schedules = Substitute.For<IWorkScheduleReader>();
    private readonly IHolidayCalendarLookup _holidays = Substitute.For<IHolidayCalendarLookup>();
    private readonly ILegislationParameterReader _legislation = Substitute.For<ILegislationParameterReader>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();

    private OvertimeRequestRules CreateRules() => new(_requests, _consents, _schedules, _holidays, _legislation, _outbox);

    private static OvertimeRequest ActiveRequest(int durationMinutes, DateOnly workDate) =>
        OvertimeRequest.Submit(
            Guid.NewGuid(), Guid.NewGuid(), workDate, new TimeOnly(9, 0), durationMinutes, OvertimeDayKind.Workday, 0, null,
            OvertimeRequestSource.Manual, DateTime.UtcNow, null, null).Value;

    /// <summary>A single OvertimeRequest is capped at OvertimeLimits.MaxDurationMinutes (24h) - split a large target
    /// total across as many same-day records as needed so the domain factory's own duration validation still passes.</summary>
    private static IReadOnlyList<OvertimeRequest> SplitIntoRequests(int totalMinutes, DateOnly workDate)
    {
        var requests = new List<OvertimeRequest>();
        var remaining = totalMinutes;
        while (remaining > 0)
        {
            var chunk = Math.Min(remaining, OvertimeLimits.MaxDurationMinutes);
            requests.Add(ActiveRequest(chunk, workDate));
            remaining -= chunk;
        }

        return requests;
    }

    private void SetupYearTotal(int year, int existingMinutes)
    {
        var yearStart = new DateOnly(year, 1, 1);
        _requests.ListActiveAsync(_employeeId, yearStart, yearStart.AddYears(1).AddDays(-1), Arg.Any<CancellationToken>())
            .Returns(existingMinutes == 0 ? [] : SplitIntoRequests(existingMinutes, yearStart));
    }

    [Fact]
    public async Task GetAnnualUsageAsync_JustUnder270Hours_IsNotExceeded()
    {
        // 269:59 -> 16199 dakika.
        SetupYearTotal(2026, 16199);

        var (used, limit) = await CreateRules().GetAnnualUsageAsync(_employeeId, 2026, new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        used.ShouldBe(16199);
        limit.ShouldBe(270m * 60);
        (used > limit).ShouldBeFalse();
    }

    [Fact]
    public async Task GetAnnualUsageAsync_ExactlyAt270Hours_IsNotExceeded()
    {
        // 270:00 tam sınırda - kural "aşan" (>) olduğu için henüz aşılmadı.
        SetupYearTotal(2026, 270 * 60);

        var (used, limit) = await CreateRules().GetAnnualUsageAsync(_employeeId, 2026, new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        (used > limit).ShouldBeFalse();
    }

    [Fact]
    public async Task GetAnnualUsageAsync_OneMinuteOver270Hours_IsExceeded()
    {
        // 270:01 -> sınırı aşar.
        SetupYearTotal(2026, (270 * 60) + 1);

        var (used, limit) = await CreateRules().GetAnnualUsageAsync(_employeeId, 2026, new DateOnly(2026, 12, 31), TestContext.Current.CancellationToken);

        (used > limit).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAnnualUsageAsync_NewYear_DoesNotCarryPreviousYearsUsage()
    {
        // Yıl dönümü sıfırlanması: 2026 sınırında olan bir çalışan, 2027'de sıfırdan başlar (ListActiveAsync farklı bir
        // [yearStart, yearEnd] aralığıyla çağrılır - repository'nin 2027 aralığı için hiç veri döndürmemesi yeterli).
        SetupYearTotal(2026, 270 * 60);
        _requests.ListActiveAsync(_employeeId, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), Arg.Any<CancellationToken>())
            .Returns([]);

        var (used, _) = await CreateRules().GetAnnualUsageAsync(_employeeId, 2027, new DateOnly(2027, 1, 15), TestContext.Current.CancellationToken);

        used.ShouldBe(0);
    }

    [Fact]
    public async Task GetAnnualUsageAsync_UsesLegislationLimit_WhenSeeded()
    {
        _legislation.FindAsync(LegislationParameterKeys.OvertimeAnnualLimitHours, Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LegislationParameterValue(LegislationParameterKeys.OvertimeAnnualLimitHours, LegislationCountries.Turkey, 300m, null, new DateOnly(2020, 1, 1), null, "seed"));
        SetupYearTotal(2026, 0);

        var (_, limit) = await CreateRules().GetAnnualUsageAsync(_employeeId, 2026, new DateOnly(2026, 6, 1), TestContext.Current.CancellationToken);

        limit.ShouldBe(300m * 60);
    }
}
