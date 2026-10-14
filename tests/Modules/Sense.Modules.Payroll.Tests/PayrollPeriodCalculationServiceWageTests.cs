using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Payslips;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

public sealed partial class PayrollPeriodCalculationServiceTests
{
    private static Payslip CapturedPayslip(IPayslipRepository payslips) =>
        (Payslip)payslips.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IPayslipRepository.Add)).GetArguments()[0]!;

    [Fact]
    public async Task NetMonthlyWage_PartialMonth_GrossesUpTheProratedNetInsteadOfProratingAFullMonthGross()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaries);
        var id = Guid.NewGuid();
        GivenEmployee(profiles, salaries, id, new(45_000m, "TRY", EmployeeSalaryType.Net, EmployeePayFrequency.Monthly, new DateOnly(2026, 1, 1)),
            hireDate: new DateOnly(2026, 9, 16));

        var result = await service.RecalculateAllAsync(SeptemberPeriod(), [id], CancellationToken.None);

        result.CalculatedCount.ShouldBe(1);
        Math.Abs(CapturedPayslip(payslips).NetAmount - 22_500m).ShouldBeLessThanOrEqualTo(0.01m);
    }

    [Fact]
    public async Task NetDailyWage_PartialMonth_KeepsTheAgreedNetForThePaidDays()
    {
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaries);
        var id = Guid.NewGuid();
        GivenWageEmployee(profiles, salaries, id, WageBasis.Daily,
            WageSalary(EmployeePayFrequency.Daily, 1_500m, EmployeeSalaryType.Net), new DateOnly(2026, 9, 16));

        var result = await service.RecalculateAllAsync(SeptemberPeriod(), [id], CancellationToken.None);

        result.CalculatedCount.ShouldBe(1);
        Math.Abs(CapturedPayslip(payslips).NetAmount - 22_500m).ShouldBeLessThanOrEqualTo(0.01m);
        var earnings = CapturedSteps(traces).Single(s => s.Code == PayrollTraceStepCodes.Earnings);
        earnings.Inputs["netTarget"].ShouldBe("22500");
        earnings.Inputs["paidDays"].ShouldBe("15");
    }

    [Fact]
    public async Task NetHourlyWage_UsesApprovedFractionalHours_WithoutRoundingTheGrossUnitRate()
    {
        var service = BuildService(out var payslips, out var traces, out var profiles, out _, out var salaries, out var attendance);
        var id = Guid.NewGuid();
        GivenWageEmployee(profiles, salaries, id, WageBasis.Hourly, WageSalary(EmployeePayFrequency.Hourly, 200m, EmployeeSalaryType.Net));
        attendance.GetAsync(TenantId, id, 2026, 9, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkedTimeSummary?>(new(24, 2, 4m, NormalHoursShortfall: 1.25m, IsTimesheetApproved: true)));

        var result = await service.RecalculateAllAsync(SeptemberPeriod(), [id], CancellationToken.None);

        result.CalculatedCount.ShouldBe(1);
        // (30 - 2) * 7.5 - 1.25 = 208.75 paid normal hours; raw overtime does not enter the base wage.
        Math.Abs(CapturedPayslip(payslips).NetAmount - 41_750m).ShouldBeLessThanOrEqualTo(0.01m);
        var earnings = CapturedSteps(traces).Single(s => s.Code == PayrollTraceStepCodes.Earnings);
        earnings.Inputs["paidHours"].ShouldBe("208.75");
        earnings.Inputs["netUnitRate"].ShouldBe("200");
        earnings.Inputs["paidHoursSource"].ShouldBe("approvedAttendance");
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<PayrollInputSnapshotDto>(CapturedPayslip(payslips).InputSnapshot,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        snapshot!.HourlyAttendance!.NormalHoursShortfall.ShouldBe(1.25m);
    }

    [Theory]
    [InlineData(false, PayrollSkipReasons.HourlyAttendanceMissing)]
    [InlineData(true, PayrollSkipReasons.HourlyAttendanceNotApproved)]
    public async Task HourlyWage_DoesNotInventHours_WhenAttendanceIsMissingOrUnapproved(bool hasData, string reason)
    {
        var service = BuildService(out var payslips, out _, out var profiles, out var dirtyFlags, out var salaries, out var attendance);
        var id = Guid.NewGuid();
        GivenWageEmployee(profiles, salaries, id, WageBasis.Hourly, WageSalary(EmployeePayFrequency.Hourly, 200m));
        if (hasData)
            attendance.GetAsync(TenantId, id, 2026, 9, null, null, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<WorkedTimeSummary?>(new(26, 0, 0m, NormalHoursShortfall: 0m)));

        var result = await service.RecalculateAllAsync(SeptemberPeriod(), [id], CancellationToken.None);

        result.Skipped.ShouldBe([new PayrollSkippedEmployee(id, reason)]);
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
        dirtyFlags.Received(1).Add(Arg.Is<PayslipDirtyFlag>(flag => flag.EmployeeId == id
            && flag.Reasons.Contains(PayslipDirtyFlagReasons.CalculationBlocked)));
    }

    [Fact]
    public async Task HourlyWage_OnlyApprovedOvertimeAndHolidayWorkBecomeAdditionalEarnings()
    {
        var id = Guid.NewGuid();
        var overtime = Substitute.For<Sense.Modules.Leave.Contracts.IOvertimePayrollReader>();
        overtime.GetAsync(TenantId, 2026, 9, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Sense.Modules.Leave.Contracts.OvertimePayrollLine>>(
                [new(id, 0, 600, 0)]));
        var service = BuildService(out var payslips, out _, out var profiles, out _, out var salaries,
            out var attendance, out _, out _, out _, overtimeOverride: overtime);
        GivenWageEmployee(profiles, salaries, id, WageBasis.Hourly, WageSalary(EmployeePayFrequency.Hourly, 200m));
        attendance.GetAsync(TenantId, id, 2026, 9, null, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<WorkedTimeSummary?>(new(26, 0, 14m, PublicHolidayDaysWorked: 1,
                NormalHoursShortfall: 1.5m, IsTimesheetApproved: true)));

        var result = await service.RecalculateAllAsync(SeptemberPeriod(), [id], CancellationToken.None);

        result.CalculatedCount.ShouldBe(1);
        // Normal 223.5 * 200 + approved overtime 10 * 200 * 1.5 + holiday 7.5 * 200.
        CapturedPayslip(payslips).GrossAmount.ShouldBe(49_200m);
    }

    [Fact]
    public async Task PayrollSalaryLookup_StopsAtTheExitDate_NotTheEndOfTheMonth()
    {
        var service = BuildService(out _, out _, out var profiles, out _, out var salaries);
        var id = Guid.NewGuid();
        var exit = new DateOnly(2026, 9, 10);
        GivenEmployee(profiles, salaries, id, terminationDate: exit);

        await service.RecalculateAllAsync(SeptemberPeriod(), [id], CancellationToken.None);

        await salaries.Received(1).GetSalaryAsOfAsync(TenantId, id, exit, Arg.Any<CancellationToken>());
        await salaries.DidNotReceive().GetSalaryAsOfAsync(TenantId, id, new DateOnly(2026, 9, 30), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void HourlyAttendance_CorrectionsChangeTheStoredInputHash()
    {
        var profile = ProfileWithWageBasis(Guid.NewGuid(), WageBasis.Hourly);
        var salary = WageSalary(EmployeePayFrequency.Hourly, 200m);
        var original = new WorkedTimeSummary(26, 0, 0m, NormalHoursShortfall: 0m, IsTimesheetApproved: true);
        var before = PayrollInputHashing.BuildSnapshot(profile, salary, hourlyAttendance: original);
        var after = PayrollInputHashing.BuildSnapshot(profile, salary, hourlyAttendance: original with { NormalHoursShortfall = 2m, IsTimesheetApproved = false });

        PayrollInputHashing.Hash(before).ShouldNotBe(PayrollInputHashing.Hash(after));
    }

    [Fact]
    public async Task Preview_WithMissingHourlyAttendance_DoesNotWriteABlockingFlag()
    {
        var service = BuildService(out var payslips, out _, out var profiles, out var dirtyFlags, out var salaries);
        var id = Guid.NewGuid();
        GivenWageEmployee(profiles, salaries, id, WageBasis.Hourly, WageSalary(EmployeePayFrequency.Hourly, 200m));

        var result = await service.PreviewAsync(SeptemberPeriod(), [id], CancellationToken.None);

        result.Summary.Skipped.ShouldBe([new PayrollSkippedEmployee(id, PayrollSkipReasons.HourlyAttendanceMissing)]);
        dirtyFlags.DidNotReceive().Add(Arg.Any<PayslipDirtyFlag>());
        payslips.DidNotReceive().Add(Arg.Any<Payslip>());
    }
}
