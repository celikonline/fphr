using NSubstitute;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

/// <summary>
/// Regression: overtime submission must reject an undefined pay-rate kind (used to persist "99") and a work date
/// that has not been worked yet (tenant-local date), and a single record stays capped at 24 hours.
/// </summary>
public sealed class OvertimeSubmitValidationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    // 2026-09-19 20:30 UTC = 23:30 in Istanbul (UTC+3, same day) but 2026-09-20 in UTC+14.
    private static readonly DateTime Now = new(2026, 9, 19, 20, 30, 0, DateTimeKind.Utc);

    private readonly IOvertimeRequestRepository _requests = Substitute.For<IOvertimeRequestRepository>();
    private readonly IOvertimeConsentRepository _consents = Substitute.For<IOvertimeConsentRepository>();
    private readonly IWorkScheduleReader _schedules = Substitute.For<IWorkScheduleReader>();
    private readonly IHolidayCalendarLookup _holidays = Substitute.For<IHolidayCalendarLookup>();
    private readonly ILegislationParameterReader _legislation = Substitute.For<ILegislationParameterReader>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly ITenantTimeZoneLookup _timeZones = Substitute.For<ITenantTimeZoneLookup>();

    private OvertimeRequestRules CreateRules(string? timeZoneId)
    {
        _timeZones.FindTimeZoneIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(timeZoneId);
        _requests.ListActiveAsync(EmployeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _consents.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns([]);
        _schedules.GetScheduledIntervalsAsync(TenantId, EmployeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _schedules.GetWorkingDayPredicateAsync(TenantId, EmployeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((Func<DateOnly, bool>)(_ => true));
        _holidays.ResolveCalendarAsync(TenantId, null, null, Arg.Any<CancellationToken>()).Returns((HolidayCalendarRef?)null);
        _legislation.FindAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((LegislationParameterValue?)null);
        _workflow.StartAsync(Arg.Any<string>(), Arg.Any<Guid>(), EmployeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Guid.NewGuid()));
        return new OvertimeRequestRules(_requests, _consents, _schedules, _holidays, _legislation, _outbox, _timeZones);
    }

    private static Task<Result<(OvertimeRequest Request, OvertimeSubmissionDto Submission)>> SubmitAsync(
        OvertimeRequestRules rules, IWorkflowService workflow, DateOnly workDate, int minutes = 60, OvertimePayRateKind rate = OvertimePayRateKind.Rate50) =>
        rules.SubmitAsync(workflow, TenantId, EmployeeId, workDate, new TimeOnly(19, 0), minutes, null, OvertimeRequestSource.Manual, Now, null, null, CancellationToken.None, rate);

    [Fact]
    public async Task FutureWorkDate_IsRejected_AndNoApprovalIsStarted()
    {
        var rules = CreateRules("Europe/Istanbul");

        var result = await SubmitAsync(rules, _workflow, new DateOnly(2026, 9, 20));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(OvertimeErrors.FutureDate);
        await _workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task TodayAndPastWorkDates_AreAccepted()
    {
        var rules = CreateRules("Europe/Istanbul");

        (await SubmitAsync(rules, _workflow, new DateOnly(2026, 9, 19))).IsSuccess.ShouldBeTrue();
        (await SubmitAsync(rules, _workflow, new DateOnly(2026, 9, 18))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task TenantLocalDate_DecidesWhatIsFuture()
    {
        // At 20:30 UTC it is already 2026-09-20 in UTC+14, so 09-20 is "today" there but still the future for a UTC/unknown zone.
        (await SubmitAsync(CreateRules("Pacific/Kiritimati"), _workflow, new DateOnly(2026, 9, 20))).IsSuccess.ShouldBeTrue();
        (await SubmitAsync(CreateRules(null), _workflow, new DateOnly(2026, 9, 20))).Error.Code.ShouldBe(OvertimeErrors.FutureDate);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void Domain_Submit_RejectsUndefinedPayRateKind(int rate)
    {
        var result = OvertimeRequest.Submit(
            TenantId, EmployeeId, new DateOnly(2026, 9, 10), new TimeOnly(17, 0), 60, OvertimeDayKind.Workday, 0, null,
            OvertimeRequestSource.Manual, Now, null, null, (OvertimePayRateKind)rate);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LeaveDomainErrors.OvertimeInvalidPayRateKind);
    }

    [Fact]
    public void Domain_ChangePayRateKind_RejectsUndefinedValue_AndKeepsTheOldRate()
    {
        var request = OvertimeRequest.Submit(
            TenantId, EmployeeId, new DateOnly(2026, 9, 10), new TimeOnly(17, 0), 60, OvertimeDayKind.Workday, 0, null,
            OvertimeRequestSource.Manual, Now, null, null).Value;

        var result = request.ChangePayRateKind((OvertimePayRateKind)99);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LeaveDomainErrors.OvertimeInvalidPayRateKind);
        request.PayRateKind.ShouldBe(OvertimePayRateKind.Rate50);
        request.PayRateMultiplier.ShouldBe(1.5m);
    }

    [Fact]
    public void Validators_RejectUndefinedPayRateKind()
    {
        new SubmitMyOvertimeRequestValidator()
            .Validate(new SubmitMyOvertimeRequestCommand(new DateOnly(2026, 9, 10), new TimeOnly(17, 0), 60, null, (OvertimePayRateKind)99))
            .IsValid.ShouldBeFalse();
        new CreateOvertimeRequestOnBehalfValidator()
            .Validate(new CreateOvertimeRequestOnBehalfCommand(EmployeeId, new DateOnly(2026, 9, 10), new TimeOnly(17, 0), 60, null, (OvertimePayRateKind)99))
            .IsValid.ShouldBeFalse();
        new ChangeOvertimePayRateKindValidator().Validate(new ChangeOvertimePayRateKindCommand(Guid.NewGuid(), (OvertimePayRateKind)99)).IsValid.ShouldBeFalse();
        new SubmitMyOvertimeRequestValidator()
            .Validate(new SubmitMyOvertimeRequestCommand(new DateOnly(2026, 9, 10), new TimeOnly(17, 0), 60, null, OvertimePayRateKind.Rate25))
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1440, true)]
    [InlineData(1441, false)]
    public void Validator_CapsASingleRecordAtTwentyFourHours(int minutes, bool valid)
    {
        new SubmitMyOvertimeRequestValidator()
            .Validate(new SubmitMyOvertimeRequestCommand(new DateOnly(2026, 9, 10), new TimeOnly(17, 0), minutes, null))
            .IsValid.ShouldBe(valid);
    }
}
