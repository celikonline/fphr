using FluentValidation.TestHelper;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Domain;

/// <summary>W0-a enum genişletmeleri: <c>Incalculable</c> çözüm önerisi ve yönsüz okutmanın self-servis girişte reddi.</summary>
public sealed class IncalculableAndDirectionTests
{
    [Fact]
    public void Incalculable_day_only_offers_manual_correction_or_ignore()
    {
        var actions = AnomalyResolutionAdvisor.AllowedActions(AttendanceAnomaly.Incalculable);

        actions.ShouldBe([AnomalyResolutionAction.Ignore, AnomalyResolutionAction.SetWorkedHours], ignoreOrder: true);
    }

    [Fact]
    public void Incalculable_flag_is_a_distinct_bit_after_worked_while_on_leave()
    {
        ((int)AttendanceAnomaly.Incalculable).ShouldBe(1 << 10);
        ((int)AttendanceDaySource.Request).ShouldBe(4);
        ((int)AttendanceDaySource.Terminal).ShouldBe(5);
        ((int)PunchDirection.Unknown).ShouldBe(4); // BreakStart = 2, BreakEnd = 3 (dev); stored as string, so numbers never reach the DB
    }

    [Fact]
    public void Self_check_in_validators_reject_unknown_direction()
    {
        new QrCheckInValidator().TestValidate(new QrCheckInCommand("QR-1", PunchDirection.Unknown)).ShouldHaveValidationErrorFor(x => x.Direction);
        new GpsCheckInValidator().TestValidate(new GpsCheckInCommand(41, 29, null, PunchDirection.Unknown)).ShouldHaveValidationErrorFor(x => x.Direction);
        new QrCheckInValidator().TestValidate(new QrCheckInCommand("QR-1", PunchDirection.In)).ShouldNotHaveValidationErrorFor(x => x.Direction);
    }
}
