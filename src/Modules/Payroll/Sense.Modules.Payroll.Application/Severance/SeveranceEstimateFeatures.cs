using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Payroll.Application.Calculation;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Severance;

/// <summary>One trace step of a severance estimate, mirroring Payroll's Payslip trace shape (11.3.7) for the same read-only-audit-document purpose.</summary>
public sealed record SeveranceTraceStepDto(int Order, string Code, string TitleKey, IReadOnlyDictionary<string, string> Inputs, string Formula, decimal Output);

/// <summary>
/// Read-only kıdem/ihbar/kullanılmamış izin ücreti estimate ("İşten ayrılış sihirbazı" summary panel, senseik-011
/// F-06). Nothing is persisted - a hypothetical exit date/cause the caller is still deciding on.
/// </summary>
public sealed record SeveranceEstimateDto(
    decimal SeveranceAmount,
    decimal NoticeAmount,
    decimal UnusedLeaveAmount,
    decimal TotalAmount,
    int SeniorityDays,
    decimal DailyGrossWage,
    string Currency,
    IReadOnlyList<SeveranceTraceStepDto> Steps);

/// <summary>
/// Salt-okunur tahmin: mutasyon yok, outbox gerekmez (11.3.03 "İşten ayrılış sihirbazı" özet bloğu). Employee cannot
/// depend on Payroll (module boundary), so this query lives on the Payroll side and reads Employee/Leave only
/// through their Contracts ports.
/// </summary>
[RequiresPermission("payroll.severance.estimate")]
public sealed record GetSeveranceEstimateQuery(Guid EmployeeId, DateOnly TerminationDate, SeveranceCauseCategory CauseCategory) : IQuery<SeveranceEstimateDto>;

public sealed class GetSeveranceEstimateHandler(ITenantContext tenant, SeveranceEstimator estimator) : IQueryHandler<GetSeveranceEstimateQuery, SeveranceEstimateDto>
{
    public Task<Result<SeveranceEstimateDto>> Handle(GetSeveranceEstimateQuery query, CancellationToken cancellationToken) =>
        estimator.EstimateAsync(tenant.TenantId, query.EmployeeId, query.TerminationDate, query.CauseCategory, cancellationToken);
}

/// <summary>
/// The kıdem/ihbar/izin computation shared by the read-only wizard estimate (<see cref="GetSeveranceEstimateHandler"/>)
/// and the frozen exit settlement written when <c>EmployeeTerminated</c> arrives (<c>EmployeeTerminatedSettlementHandler</c>),
/// so both always agree. Takes the tenant explicitly because the event handler has no request-scoped tenant.
/// </summary>
public sealed class SeveranceEstimator(
    IEmployeeSalaryLookup salaryLookup,
    IEmployeeSeniorityLookup seniorityLookup,
    ILeaveBalanceLookup leaveBalanceLookup,
    ILegislationParameterReader legislation,
    TimeProvider clock)
{
    public async Task<Result<SeveranceEstimateDto>> EstimateAsync(
        Guid tenantId, Guid employeeId, DateOnly terminationDate, SeveranceCauseCategory causeCategory, CancellationToken cancellationToken)
    {
        // A hypothetical exit date must not precede the hire date nor lie more than ~1 year ahead (nonsense estimates such as year 2100).
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var employment = await salaryLookup.GetEmploymentDatesAsync(tenantId, employeeId, cancellationToken);
        if ((employment?.HireDate is { } hireDate && terminationDate < hireDate)
            || terminationDate > today.AddDays(PayrollLimits.MaxSeveranceTerminationDaysAhead))
        {
            return Error.Validation(PayrollDomainErrors.SeveranceTerminationDateInvalid);
        }

        var salary = await salaryLookup.GetCurrentSalaryAsync(tenantId, employeeId, cancellationToken);
        if (salary is null)
        {
            return Error.Validation(PayrollDomainErrors.SeveranceEstimateSalaryMissing);
        }

        var seniorityDays = await seniorityLookup.GetCountedSeniorityDaysAsync(
            tenantId, employeeId, EmployeeSeniorityPurpose.Severance, terminationDate, cancellationToken);
        var unusedLeaveDays = await leaveBalanceLookup.GetRemainingAnnualLeaveDaysAsync(tenantId, employeeId, cancellationToken) ?? 0m;

        var dailyGrossWage = DailyGrossWage(salary);
        SeveranceLegislationSnapshot snapshot;
        try
        {
            snapshot = await SeveranceLegislationSnapshotResolver.ResolveAsync(legislation, terminationDate, cancellationToken);
        }
        catch (LegislationParameterMissingException ex)
        {
            return LegislationMissingError.From(ex);
        }

        var input = new SeveranceInput(seniorityDays, causeCategory, dailyGrossWage, unusedLeaveDays);
        var result = SeveranceCalculator.Calculate(input, snapshot);

        return new SeveranceEstimateDto(
            result.SeveranceAmount, result.NoticeAmount, result.UnusedLeaveAmount, result.TotalAmount,
            seniorityDays, dailyGrossWage, salary.Currency,
            result.Steps.Select(s => new SeveranceTraceStepDto(s.Order, s.Code, s.TitleKey, s.Inputs, s.Formula, s.Output)).ToList());
    }

    /// <summary>
    /// Daily basis for the estimate, derived from the employee's current salary record's pay frequency. TODO
    /// (net-to-gross, out of this slice's scope): a Net-typed record is used as-is - the law's basis is the last
    /// *gross* wage, so a Net record understates the estimate; flagged rather than silently wrong-but-unmentioned,
    /// mirrors PayrollCalculationInput's own documented simplifications (full-worked-month, WageBasis=Monthly only).
    /// </summary>
    private static decimal DailyGrossWage(EmployeeCurrentSalary salary) => salary.PayFrequency switch
    {
        EmployeePayFrequency.Daily => salary.Amount,
        EmployeePayFrequency.Weekly => salary.Amount / 7m,
        EmployeePayFrequency.Monthly => salary.Amount / 30m,
        EmployeePayFrequency.Yearly => salary.Amount / 360m,
        EmployeePayFrequency.Hourly => salary.Amount * 8m,
        _ => salary.Amount / 30m,
    };
}
