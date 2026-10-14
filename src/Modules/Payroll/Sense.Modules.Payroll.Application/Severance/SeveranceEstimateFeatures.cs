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
    IReadOnlyList<SeveranceTraceStepDto> Steps,
    decimal RegularBenefitsMonthlyGross = 0m);

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
    TimeProvider clock,
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IEmployeeDisabilityLookup disabilityLookup,
    IEmployeePayrollProfileRepository profiles)
{
    public async Task<Result<SeveranceEstimateDto>> EstimateAsync(
        Guid tenantId, Guid employeeId, DateOnly terminationDate, SeveranceCauseCategory causeCategory, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(causeCategory))
        {
            return Error.Validation(PayrollDomainErrors.SeveranceEstimateCauseInvalid);
        }

        // A hypothetical exit date must not precede the hire date nor lie more than ~1 year ahead (nonsense estimates such as year 2100).
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var employment = await salaryLookup.GetEmploymentDatesAsync(tenantId, employeeId, cancellationToken);
        if ((employment?.HireDate is { } hireDate && terminationDate < hireDate)
            || terminationDate > today.AddDays(PayrollLimits.MaxSeveranceTerminationDaysAhead))
        {
            return Error.Validation(PayrollDomainErrors.SeveranceTerminationDateInvalid);
        }

        var salary = await salaryLookup.GetSalaryAsOfAsync(tenantId, employeeId, terminationDate, cancellationToken);
        if (salary is null)
        {
            return Error.Validation(PayrollDomainErrors.SeveranceEstimateSalaryMissing);
        }

        if (salary.Amount <= 0m || !Enum.IsDefined(salary.PayFrequency) || !Enum.IsDefined(salary.SalaryType)
            || !string.Equals(salary.Currency, "TRY", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation(PayrollDomainErrors.SeveranceEstimateSalaryUnsupported);
        }

        var seniorityDays = await seniorityLookup.GetCountedSeniorityDaysAsync(
            tenantId, employeeId, EmployeeSeniorityPurpose.Severance, terminationDate, cancellationToken);
        var leaveBalance = await leaveBalanceLookup.GetRemainingAnnualLeaveDaysAsOfAsync(tenantId, employeeId, terminationDate, cancellationToken);
        if (leaveBalance is null)
        {
            return Error.Rule(PayrollDomainErrors.SeveranceEstimateLeaveBalanceMissing);
        }

        var unusedLeaveDays = Math.Max(0m, leaveBalance.Value);

        var dailyGrossWage = DailyWage(salary);
        SeveranceLegislationSnapshot snapshot;
        decimal priorCumulativeBase = 0m;
        try
        {
            snapshot = await SeveranceLegislationSnapshotResolver.ResolveAsync(legislation, terminationDate, cancellationToken);
            if (salary.SalaryType == EmployeeSalaryType.Net)
            {
                var payrollSnapshot = await PayrollLegislationSnapshotResolver.ResolveAsync(legislation, terminationDate, cancellationToken);
                priorCumulativeBase = await PriorCumulativeTaxBase.ReadAsync(periods, payslips, employeeId, terminationDate, cancellationToken);
                var disabilityDegree = await disabilityLookup.GetDegreeAsync(tenantId, employeeId, cancellationToken) ?? EmployeeDisabilityDegree.None;
                var grossMonthly = NetToGrossSolver.Solve(dailyGrossWage * EmploymentProration.PayrollMonthDays,
                    gross => PayrollCalculator.Calculate(new PayrollCalculationInput(
                        GrossMonthlySalary: gross, WorkedDays: EmploymentProration.PayrollMonthDays,
                        CalendarDaysInMonth: DateTime.DaysInMonth(terminationDate.Year, terminationDate.Month),
                        MealVariant: MealBenefitVariant.None, MealDailyAmount: 0m,
                        PriorCumulativeGvBase: priorCumulativeBase, AsOfDate: terminationDate, DisabilityDegree: (int)disabilityDegree), payrollSnapshot));
                dailyGrossWage = grossMonthly / EmploymentProration.PayrollMonthDays;
            }
        }
        catch (LegislationParameterMissingException ex)
        {
            return LegislationMissingError.From(ex);
        }

        var profile = await profiles.FindCurrentAsync(employeeId, terminationDate, cancellationToken);
        if (profile is not null && (profile.EmployeeId != employeeId || profile.TenantId != tenantId))
            return Error.NotFound(PayrollDomainErrors.ProfileNotFound);
        var regularBenefits = profile?.RegularBenefitsMonthlyGross ?? 0m;
        var input = new SeveranceInput(seniorityDays, causeCategory, dailyGrossWage, unusedLeaveDays, regularBenefits);
        var result = SeveranceCalculator.Calculate(input, snapshot);

        var wageInputs = new Dictionary<string, string>
        {
            ["salaryAmount"] = salary.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["salaryType"] = salary.SalaryType.ToString(),
            ["payFrequency"] = salary.PayFrequency.ToString(),
            ["salaryEffectiveFrom"] = salary.EffectiveFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["asOfDate"] = terminationDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["priorCumulativeGvBase"] = priorCumulativeBase.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["regularBenefitsMonthlyGross"] = regularBenefits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["dressedDailyGrossWage"] = (dailyGrossWage + regularBenefits / 30m).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var steps = new List<SeveranceTraceStepDto>
        {
            new(1, "WAGE_BASIS", "payroll.severance.trace.wage_basis", wageInputs,
                salary.SalaryType == EmployeeSalaryType.Net ? "çıkış tarihindeki net ücretin dönem parametreleriyle brütleştirilmesi / 30" : "çıkış tarihindeki ücretin günlük brüt karşılığı",
                decimal.Round(dailyGrossWage, 6, MidpointRounding.AwayFromZero))
        };
        if (profile is not null)
        {
            wageInputs["payrollProfileId"] = profile.Id.ToString();
            wageInputs["profileValidFrom"] = profile.ValidFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }
        steps.AddRange(result.Steps.Select(s => new SeveranceTraceStepDto(s.Order + 1, s.Code, s.TitleKey, s.Inputs, s.Formula, s.Output)));

        return new SeveranceEstimateDto(
            result.SeveranceAmount, result.NoticeAmount, result.UnusedLeaveAmount, result.TotalAmount,
            seniorityDays, dailyGrossWage, salary.Currency,
            steps, regularBenefits);
    }

    /// <summary>
    /// Daily equivalent before gross-up, from the salary in force on the exit date. The hourly conversion shares the payroll basis.
    /// </summary>
    private static decimal DailyWage(EmployeeCurrentSalary salary) => salary.PayFrequency switch
    {
        EmployeePayFrequency.Daily => salary.Amount,
        EmployeePayFrequency.Weekly => salary.Amount / 7m,
        EmployeePayFrequency.Monthly => salary.Amount / 30m,
        EmployeePayFrequency.Yearly => salary.Amount / 360m,
        EmployeePayFrequency.Hourly => salary.Amount * PeriodWageMath.DailyWorkingHours,
        _ => throw new NotSupportedException($"Unsupported pay frequency '{salary.PayFrequency}'."),
    };
}
