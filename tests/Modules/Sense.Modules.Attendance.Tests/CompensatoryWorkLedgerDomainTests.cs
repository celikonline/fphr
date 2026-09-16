using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Compensatory;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class CompensatoryWorkLedgerDomainTests
{
    private static readonly DateOnly WeekStart = new(2026, 9, 21);
    private static readonly DateOnly DueDate = WeekStart.AddMonths(CompensatoryWorkLimits.WindowMonths);

    private static CompensatoryWorkLedger Open(int deficitMinutes = 120) =>
        CompensatoryWorkLedger.Open(Guid.NewGuid(), Guid.NewGuid(), WeekStart, deficitMinutes, DueDate);

    [Fact]
    public void Open_StartsWithZeroCompensatedAndOpenStatus()
    {
        var ledger = Open(120);

        ledger.DeficitMinutes.ShouldBe(120);
        ledger.CompensatedMinutes.ShouldBe(0);
        ledger.Status.ShouldBe(CompensatoryWorkStatus.Open);
    }

    [Fact]
    public void RecordCompensation_PartialAmount_StaysOpen()
    {
        var ledger = Open(120);

        var result = ledger.RecordCompensation(60);

        result.IsSuccess.ShouldBeTrue();
        ledger.CompensatedMinutes.ShouldBe(60);
        ledger.Status.ShouldBe(CompensatoryWorkStatus.Open);
    }

    [Fact]
    public void RecordCompensation_ReachingDeficit_Completes()
    {
        var ledger = Open(120);

        ledger.RecordCompensation(120);

        ledger.Status.ShouldBe(CompensatoryWorkStatus.Completed);
    }

    [Fact]
    public void RecordCompensation_OverDeficit_CapsAtDeficit()
    {
        var ledger = Open(120);

        ledger.RecordCompensation(200);

        ledger.CompensatedMinutes.ShouldBe(120);
        ledger.Status.ShouldBe(CompensatoryWorkStatus.Completed);
    }

    [Fact]
    public void RecordCompensation_ZeroOrNegative_Fails()
    {
        var ledger = Open(120);

        var result = ledger.RecordCompensation(0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.CompensatoryMinutesInvalid);
    }

    [Fact]
    public void RecordCompensation_AlreadyCompleted_Fails()
    {
        var ledger = Open(120);
        ledger.RecordCompensation(120);

        var result = ledger.RecordCompensation(10);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.CompensatoryLedgerNotOpen);
    }

    [Fact]
    public void AdjustDeficit_CompensatedAlreadyCoversNewLowerDeficit_Completes()
    {
        var ledger = Open(120);
        ledger.RecordCompensation(90);

        var result = ledger.AdjustDeficit(90);

        result.IsSuccess.ShouldBeTrue();
        ledger.Status.ShouldBe(CompensatoryWorkStatus.Completed);
    }

    [Fact]
    public void AdjustDeficit_OnCompletedLedger_Fails()
    {
        var ledger = Open(120);
        ledger.RecordCompensation(120);

        var result = ledger.AdjustDeficit(60);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.CompensatoryLedgerNotOpen);
    }

    [Fact]
    public void ExpireIfPastDue_PastDueDate_Expires()
    {
        var ledger = Open(120);

        ledger.ExpireIfPastDue(DueDate.AddDays(1));

        ledger.Status.ShouldBe(CompensatoryWorkStatus.Expired);
    }

    [Fact]
    public void ExpireIfPastDue_OnOrBeforeDueDate_StaysOpen()
    {
        var ledger = Open(120);

        ledger.ExpireIfPastDue(DueDate);

        ledger.Status.ShouldBe(CompensatoryWorkStatus.Open);
    }

    [Fact]
    public void ExpireIfPastDue_AlreadyCompleted_StaysCompleted()
    {
        var ledger = Open(120);
        ledger.RecordCompensation(120);

        ledger.ExpireIfPastDue(DueDate.AddYears(1));

        ledger.Status.ShouldBe(CompensatoryWorkStatus.Completed);
    }
}
