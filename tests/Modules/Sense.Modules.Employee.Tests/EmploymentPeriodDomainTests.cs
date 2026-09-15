using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Shouldly;
using Xunit;
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Employment periods and merged seniority ("Çalışma dönemleri ve kıdem birleştirme", Faz 1a E5a / 11.3.03): the
/// aggregate rules only - the command/API side is covered by EmploymentPeriodIntegrationTests.
/// </summary>
public sealed class EmploymentPeriodDomainTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ReasonId = Guid.NewGuid();

    [Fact]
    public void Terminate_ClosesTheOpenPeriodWithItsSettlementFlags()
    {
        var hireDate = new DateOnly(2024, 1, 1);
        var exitDate = new DateOnly(2024, 12, 31);
        var employee = Hired(hireDate);

        employee.EnsureInitialEmploymentPeriod().ShouldNotBeNull();
        employee.EnsureInitialEmploymentPeriod().ShouldBeNull("periods are materialized once");

        employee.Terminate(exitDate, ReasonId, TerminationType.Resignation, "3", severancePaid: true, leaveSettledAtExit: true).IsSuccess.ShouldBeTrue();

        var period = employee.EmploymentTimeline.Single();
        period.EndDate.ShouldBe(exitDate);
        period.TerminationReasonId.ShouldBe(ReasonId);
        period.SeverancePaid.ShouldBeTrue();
        period.LeaveSettledAtExit.ShouldBeTrue();
        period.IsOpen.ShouldBeFalse();
        period.CountedDays(exitDate).ShouldBe(366, "2024 is a leap year and both ends count");
    }

    [Fact]
    public void Rehire_OnlyFromTerminatedAndAfterThePreviousExit()
    {
        var hireDate = new DateOnly(2024, 1, 1);
        var active = Hired(hireDate);
        active.Rehire(new DateOnly(2025, 1, 1), mergePreviousPeriods: true).Error.Code.ShouldBe(EmployeeDomainErrors.RehireInvalidStatus);

        var exitDate = new DateOnly(2024, 12, 31);
        active.Terminate(exitDate, ReasonId, TerminationType.Resignation, null).IsSuccess.ShouldBeTrue();
        active.Rehire(exitDate, mergePreviousPeriods: true).Error.Code.ShouldBe(EmployeeDomainErrors.RehireDateBeforePreviousExit);

        var rehireDate = new DateOnly(2025, 7, 1);
        var outcome = active.Rehire(rehireDate, mergePreviousPeriods: true);

        outcome.IsSuccess.ShouldBeTrue();
        active.Status.ShouldBe(EmployeeStatus.Active);
        active.HireDate.ShouldBe(rehireDate);
        active.TerminationDate.ShouldBeNull();
        active.TerminationReasonId.ShouldBeNull();
        active.TerminationType.ShouldBeNull();
        active.SgkExitCode.ShouldBeNull();

        var periods = active.EmploymentTimeline;
        periods.Count.ShouldBe(2);
        periods[1].Source.ShouldBe(EmploymentPeriodSource.Rehire);
        periods[1].IsOpen.ShouldBeTrue();

        // The first period counted 366 days, so the merged leave accrual start moves back that far from the new hire date.
        outcome.Value.LeaveAccrualStartDate.ShouldBe(rehireDate.AddDays(-366));
        active.LeaveAccrualStartDate.ShouldBe(rehireDate.AddDays(-366));
    }

    [Fact]
    public void Rehire_WithoutMerge_StartsFreshAndAnExplicitDateAlwaysWins()
    {
        var withoutMerge = Terminated(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var rehireDate = new DateOnly(2025, 7, 1);
        withoutMerge.Rehire(rehireDate, mergePreviousPeriods: false).Value.LeaveAccrualStartDate.ShouldBe(rehireDate);
        withoutMerge.LeaveAccrualStartDate.ShouldBe(rehireDate);

        var overridden = Terminated(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var hrChoice = new DateOnly(2023, 6, 1);
        overridden.Rehire(rehireDate, mergePreviousPeriods: true, hrChoice).Value.LeaveAccrualStartDate.ShouldBe(hrChoice);
        overridden.LeaveAccrualStartDate.ShouldBe(hrChoice);
    }

    /// <summary>
    /// A period settled at exit (annual leave used up or paid out) drops out of leave seniority, one whose severance
    /// was paid drops out of severance seniority, and the job-security figure keeps counting both (11.3.03 / 11.3.5 C2).
    /// </summary>
    [Fact]
    public void Seniority_CountsPeriodsPerPurpose()
    {
        var employee = Hired(new DateOnly(2024, 1, 1));
        employee.EnsureInitialEmploymentPeriod();
        employee.Terminate(new DateOnly(2024, 12, 31), ReasonId, TerminationType.Resignation, null, severancePaid: true, leaveSettledAtExit: false)
            .IsSuccess.ShouldBeTrue();

        var rehireDate = new DateOnly(2025, 7, 1);
        var outcome = employee.Rehire(rehireDate, mergePreviousPeriods: true);

        // Leave counts the first period (not settled at exit), severance does not (it was paid out).
        outcome.Value.LeaveAccrualStartDate.ShouldBe(rehireDate.AddDays(-366));
        outcome.Value.SeniorityStartDate.ShouldBe(rehireDate);

        var asOf = new DateOnly(2025, 12, 31);
        employee.CountedSeniorityDays(SeniorityPurpose.Leave, asOf).ShouldBe(366 + 184);
        employee.CountedSeniorityDays(SeniorityPurpose.Severance, asOf).ShouldBe(184);
        employee.CountedSeniorityDays(SeniorityPurpose.JobSecurity, asOf).ShouldBe(366 + 184);

        // The gap between the two periods is deducted: 550 counted days end up ~18 months, not the 24 calendar months
        // between the original hire date and asOf.
        employee.SeniorityMonths(SeniorityPurpose.JobSecurity, asOf).ShouldBe(18);
        employee.SeniorityMonths(SeniorityPurpose.Severance, asOf).ShouldBe(5);
        employee.SeniorityStart(SeniorityPurpose.Severance, asOf).ShouldBe(rehireDate, "only the open period counts, and it round-trips to its own start");
    }

    /// <summary>Employees hired before employment periods existed have none stored; seniority then falls back to the hire date.</summary>
    [Fact]
    public void Seniority_WithoutStoredPeriods_FallsBackToTheHireDate()
    {
        var hireDate = new DateOnly(2024, 1, 1);
        var employee = Hired(hireDate);
        var asOf = new DateOnly(2024, 12, 31);

        employee.SeniorityStart(SeniorityPurpose.Leave, asOf).ShouldBe(hireDate);
        employee.SeniorityMonths(SeniorityPurpose.JobSecurity, asOf).ShouldBe(11);
        employee.CountedSeniorityDays(SeniorityPurpose.Severance, asOf).ShouldBe(366);

        // A leave accrual start carried back by hand (group transfer, import) counts for leave seniority only.
        var carried = EmployeeEntity.Create(TenantId, new EmployeeInfo("C", "D", HireDate: hireDate, LeaveAccrualStartDate: new DateOnly(2023, 1, 1))).Value;
        carried.SeniorityStart(SeniorityPurpose.Leave, asOf).ShouldBe(new DateOnly(2023, 1, 1));
        carried.SeniorityStart(SeniorityPurpose.Severance, asOf).ShouldBe(hireDate);

        // No hire date at all (Draft): nothing to count.
        var draft = EmployeeEntity.Create(TenantId, new EmployeeInfo("E", "F")).Value;
        draft.SeniorityStart(SeniorityPurpose.Leave, asOf).ShouldBeNull();
        draft.SeniorityMonths(SeniorityPurpose.Leave, asOf).ShouldBe(0);
    }

    private static EmployeeEntity Hired(DateOnly hireDate) =>
        EmployeeEntity.Create(TenantId, new EmployeeInfo("A", "B", HireDate: hireDate)).Value;

    private static EmployeeEntity Terminated(DateOnly hireDate, DateOnly exitDate)
    {
        var employee = Hired(hireDate);
        employee.EnsureInitialEmploymentPeriod();
        employee.Terminate(exitDate, ReasonId, TerminationType.Resignation, null).IsSuccess.ShouldBeTrue();
        return employee;
    }
}
