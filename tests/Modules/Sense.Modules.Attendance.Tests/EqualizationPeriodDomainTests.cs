using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Equalization;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class EqualizationPeriodDomainTests
{
    private static readonly DateOnly PeriodStart = new(2026, 3, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 4, 30);

    private static EqualizationPeriod OpenPeriod(int weeks = 9) =>
        EqualizationPeriod.Open(Guid.NewGuid(), Guid.NewGuid(), PeriodStart, PeriodEnd, weeks);

    [Fact]
    public void Open_SetsBudgetToWeeksTimesWeeklyBudget()
    {
        var period = OpenPeriod(weeks: 9);

        period.BudgetMinutes.ShouldBe(9 * EqualizationPeriod.WeeklyBudgetMinutes);
        period.Status.ShouldBe(EqualizationPeriodStatus.Open);
        period.TotalWorkedMinutes.ShouldBe(0);
    }

    [Fact]
    public void SetProvisionalTotal_WhileOpen_ReplacesRunningTotal()
    {
        var period = OpenPeriod();

        period.SetProvisionalTotal(1000);
        period.TotalWorkedMinutes.ShouldBe(1000);

        // Idempotent/replace semantics - a second recompute with a smaller number does not accumulate.
        period.SetProvisionalTotal(400);
        period.TotalWorkedMinutes.ShouldBe(400);
    }

    [Fact]
    public void SetProvisionalTotal_Negative_Fails()
    {
        var period = OpenPeriod();

        var result = period.SetProvisionalTotal(-1);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.EqualizationMinutesInvalid);
    }

    [Fact]
    public void Close_BeforePeriodEnd_Fails()
    {
        var period = OpenPeriod();

        var result = period.Close(PeriodEnd);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.EqualizationPeriodNotYetEnded);
    }

    [Fact]
    public void Close_AfterPeriodEnd_ComputesTrueUpOverBudget()
    {
        var period = OpenPeriod(weeks: 9); // budget = 9 * 2700 = 24300
        period.SetProvisionalTotal(25000);

        var result = period.Close(PeriodEnd.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        period.Status.ShouldBe(EqualizationPeriodStatus.Closed);
        period.TrueUpOvertimeMinutes.ShouldBe(700);
    }

    [Fact]
    public void Close_UnderBudget_TrueUpIsZero()
    {
        var period = OpenPeriod(weeks: 9);
        period.SetProvisionalTotal(10000);

        period.Close(PeriodEnd.AddDays(1));

        period.TrueUpOvertimeMinutes.ShouldBe(0);
    }

    [Fact]
    public void Close_AlreadyClosed_Fails()
    {
        var period = OpenPeriod();
        period.Close(PeriodEnd.AddDays(1));

        var result = period.Close(PeriodEnd.AddDays(2));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.EqualizationPeriodNotOpen);
    }

    [Fact]
    public void SetProvisionalTotal_AfterClose_Fails()
    {
        var period = OpenPeriod();
        period.Close(PeriodEnd.AddDays(1));

        var result = period.SetProvisionalTotal(1000);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.EqualizationPeriodNotOpen);
    }
}
