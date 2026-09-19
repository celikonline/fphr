using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Calculation;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Garnishments;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>Outcome of one <see cref="PayrollPeriodCalculationService"/> run. Employees are skipped (not counted in
/// <see cref="CalculatedCount"/>) when they have no current payroll profile/salary record, a non-Monthly WageBasis, or
/// a Net SalaryType - each a documented P1/P3 placeholder (see inline comments in <c>CalculateOneAsync</c>); this
/// slice only surfaces the aggregate count, not a per-employee reason.</summary>
public sealed record PayrollPeriodRunSummary(Guid RunId, int CalculatedCount, int SkippedCount);

/// <summary>
/// 11.3.07-bordro.md "Dönem hesabı" akışı (P3): for every candidate employee, resolves current salary + payroll
/// profile, runs <see cref="PayrollCalculator"/>, and upserts a <see cref="Payslip"/> + new-version
/// <see cref="PayslipCalculationTrace"/>. Shared by <c>RecalculateAllPayslipsCommand</c> (full run/re-run, §1/§4) and
/// <c>RecalculateDirtyPayslipsCommand</c> (§4, only flagged employees) - the only difference between the two is which
/// employee ids get fed in and whether a dirty flag is required to already exist.
/// </summary>
public sealed class PayrollPeriodCalculationService(
    IPayslipRepository payslips,
    IPayslipCalculationTraceRepository traces,
    IEmployeePayrollProfileRepository profiles,
    IPayslipDirtyFlagRepository dirtyFlags,
    IEmployeeSalaryLookup salaryLookup,
    ILegislationParameterReader legislationReader,
    ITenantContext tenant,
    TimeProvider clock,
    IWorkedTimeReader workedTimeReader,
    IAbsenceDaysReader absenceDaysReader,
    IExpensePayoutReader expensePayoutReader,
    IPayrollPeriodRepository periods,
    IGarnishmentOrderRepository garnishmentOrderRepository,
    IEmployeeDisabilityLookup disabilityLookup)
{
    private static readonly System.Text.Json.JsonSerializerOptions StepsJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>Recalculates every candidate employee (initial "Dönem aç → hesapla" run, and later "Tümünü yeniden hesapla").</summary>
    public async Task<PayrollPeriodRunSummary> RecalculateAllAsync(PayrollPeriod period, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var periodStart = new DateOnly(period.Year, period.Month, 1);
        var calendarDays = DateTime.DaysInMonth(period.Year, period.Month);
        var snapshot = await PayrollLegislationSnapshotResolver.ResolveAsync(legislationReader, periodStart, cancellationToken);

        var calculated = 0;
        var skipped = 0;
        foreach (var employeeId in employeeIds)
        {
            var outcome = await CalculateOneAsync(period, employeeId, periodStart, calendarDays, snapshot, runId, cancellationToken);
            if (outcome)
            {
                calculated++;
            }
            else
            {
                skipped++;
            }
        }

        return new PayrollPeriodRunSummary(runId, calculated, skipped);
    }

    /// <summary>Recalculates only the employees with an active dirty flag in this period ("Değişenleri yeniden hesapla", §4);
    /// clears each flag on a successful calculation. Employees whose flag cannot be resolved (no profile/salary) keep their flag.</summary>
    public async Task<PayrollPeriodRunSummary> RecalculateDirtyAsync(PayrollPeriod period, CancellationToken cancellationToken)
    {
        var activeFlags = await dirtyFlags.ListActiveByPeriodAsync(period.Id, cancellationToken);
        return await RecalculateAllAsync(period, activeFlags.Select(f => f.EmployeeId).Distinct().ToList(), cancellationToken);
    }

    private async Task<bool> CalculateOneAsync(
        PayrollPeriod period, Guid employeeId, DateOnly periodStart, int calendarDays, PayrollLegislationSnapshot snapshot, Guid runId, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindCurrentAsync(employeeId, periodStart, cancellationToken);
        if (profile is null)
        {
            return false; // no payroll profile covering this period yet
        }

        if (profile.WageBasis != WageBasis.Monthly)
        {
            return false; // PayrollCalculator only supports Monthly this slice (P1 placeholder)
        }

        var salary = await salaryLookup.GetCurrentSalaryAsync(tenant.TenantId, employeeId, cancellationToken);
        if (salary is null)
        {
            return false; // no salary record yet
        }

        if (salary.SalaryType != EmployeeSalaryType.Gross)
        {
            return false; // NET_TO_GROSS_ITER is out of scope this slice (PayrollCalculator doc)
        }

        // Cross-month cumulative GV_BASE tracking (Ocak'tan itibaren kümülatif matrah) senseik-019 2a ile devreye
        // girdi - bkz. ResolvePriorCumulativeGvBaseAsync (bu metodun altında).
        // TB-7 Attendance köprüsü (roadmap #23): null (no adapter wired yet, or Attendance has no data for this
        // employee/period) keeps today's full-month assumption; once IWorkedTimeReader has a real Attendance-backed
        // implementation, WorkedDays reflects actual attendance instead.
        var workedTime = await workedTimeReader.GetAsync(tenant.TenantId, employeeId, period.Year, period.Month, cancellationToken);

        int workedDays;
        if (workedTime is not null)
        {
            // Attendance (PDKS) already resolved a worked-day figure - use it as-is; the Leave absence bridge below
            // is skipped entirely to avoid double-counting the same missing days from two sources (G-7-08/09, faz-07
            // tur 2 madde 3 "Çift sayım kuralı").
            workedDays = workedTime.WorkedDays;
        }
        else
        {
            // No Attendance data this employee/period: fall back to İK's manually entered devamsızlık (Leave
            // AbsenceRecord, Unexcused only) as the missing-day source. Both null (no Attendance, no absence data
            // either) keeps the pre-existing full-calendar-month assumption; a null or zero absence count leaves
            // workedDays unchanged (G-7-08 PO kararı: WorkedDays bu turda yalnız bugünkü kullanım - yemek
            // pro-rasyonu - için, hafta tatili/resmi tatil ücret hakkına dokunmaz).
            var unexcusedDays = await absenceDaysReader.GetUnexcusedDaysAsync(tenant.TenantId, employeeId, period.Year, period.Month, cancellationToken);
            workedDays = unexcusedDays is > 0 ? Math.Max(0, calendarDays - unexcusedDays.Value) : calendarDays;
        }

        // TB-7 Expense köprüsü (roadmap #23, faz-07 tur 1 madde 3): port boş/null dönerse 0 - bugünkü davranış sürer.
        var expensePayout = await expensePayoutReader.GetAsync(tenant.TenantId, employeeId, period.Year, period.Month, cancellationToken);
        var expenseReimbursement = expensePayout?.ApprovedExpenseTotal ?? 0m;

        // senseik-019 2a: kümülatif GV matrahı devri - önceki dönemin Payslip'inden okunur (yıl başında veya önceki
        // dönem/payslip yoksa 0'dan başlanır, örn. yıl içinde işe giren çalışan).
        var priorCumulativeGvBase = await ResolvePriorCumulativeGvBaseAsync(period, employeeId, cancellationToken);

        // senseik-019 2a: BASİTLEŞTİRME - gerçek teşvik uygunluk kontrolü (SGK'nın güncel kanun kodu listesiyle
        // eşleşme, işyeri bazlı başvuru durumu) bu sürümde yapılmıyor, bkz. PayrollCalculationInput.IncentiveEligible.
        var incentiveEligible = !string.IsNullOrWhiteSpace(profile.IncentiveLawCode);

        // senseik-019 2b: GVK md.31 engelli çalışan gelir vergisi matrah indirimi derecesi.
        var disabilityDegree = await disabilityLookup.GetDegreeAsync(tenant.TenantId, employeeId, cancellationToken) ?? EmployeeDisabilityDegree.None;

        // senseik-019 2c: açık avans bakiyesi + aktif icra/nafaka emirleri.
        var advanceBalance = expensePayout?.OpenAdvanceBalance ?? 0m;
        var activeGarnishments = await garnishmentOrderRepository.ListActiveByEmployeeAsync(employeeId, cancellationToken) ?? [];
        var garnishmentInputs = activeGarnishments
            .Select(o => new GarnishmentDeductionInput(o.Id, o.Type, o.MonthlyInstallmentAmount, o.RemainingBalance))
            .ToList();

        var input = new PayrollCalculationInput(
            GrossMonthlySalary: salary.Amount,
            WorkedDays: workedDays,
            CalendarDaysInMonth: calendarDays,
            MealVariant: profile.MealBenefitVariant,
            MealDailyAmount: profile.MealDailyAmount,
            PriorCumulativeGvBase: priorCumulativeGvBase,
            AsOfDate: periodStart,
            ExpenseReimbursement: expenseReimbursement,
            IncentiveEligible: incentiveEligible,
            DisabilityDegree: (int)disabilityDegree,
            AdvanceDeductionTotal: advanceBalance,
            GarnishmentOrders: garnishmentInputs);

        var result = PayrollCalculator.Calculate(input, snapshot);

        // senseik-019 2c: uygulanan icra/nafaka kesintilerini emir bakiyelerine işle (aynı DbContext'te izlenen
        // entity'ler - IPayrollUnitOfWork.SaveChangesAsync çağıran command handler'ın işlemiyle birlikte kaydedilir).
        foreach (var applied in result.GarnishmentApplied)
        {
            activeGarnishments.First(o => o.Id == applied.OrderId).ApplyMonthlyDeduction(applied.Amount, periodStart);
        }

        var cumulativeGvBase = result.Step(PayrollTraceStepCodes.CumulativeBase).Output;
        var snapshotDto = PayrollInputHashing.BuildSnapshot(profile, salary);
        var inputSnapshotJson = PayrollInputHashing.Serialize(snapshotDto);
        var inputHash = PayrollInputHashing.Hash(snapshotDto);

        var existing = await payslips.FindByEmployeeAndPeriodAsync(employeeId, period.Id, cancellationToken);
        Payslip payslip;
        if (existing is null)
        {
            payslip = Payslip.CreateFirstVersion(
                tenant.TenantId, employeeId, period.Id, inputSnapshotJson, inputHash, result.GrossAmount, result.NetAmount, result.EmployerCost, cumulativeGvBase);
            payslips.Add(payslip);
        }
        else
        {
            existing.Recalculate(inputSnapshotJson, inputHash, result.GrossAmount, result.NetAmount, result.EmployerCost, cumulativeGvBase);
            payslip = existing;
        }

        var stepsJson = System.Text.Json.JsonSerializer.Serialize(result.Steps, StepsJsonOptions);
        traces.Add(PayslipCalculationTrace.Create(tenant.TenantId, payslip.Id, runId, payslip.Version, stepsJson, runId, clock.GetUtcNow().UtcDateTime));

        var activeFlag = await dirtyFlags.FindActiveAsync(period.Id, employeeId, cancellationToken);
        activeFlag?.Clear(runId, clock.GetUtcNow().UtcDateTime);

        return true;
    }

    /// <summary>senseik-019 2a: Ocak'ta (yeni takvim yılı) her zaman 0'dan başlanır; önceki ay hiç açılmamışsa veya o
    /// dönemde bu çalışan için henüz bir Payslip yoksa (örn. çalışan bu ay işe girdi) da 0 kabul edilir - yalnızca
    /// mevcut bir önceki-ay Payslip'i varsa onun CUMULATIVE_BASE çıktısı devralınır.</summary>
    private async Task<decimal> ResolvePriorCumulativeGvBaseAsync(PayrollPeriod period, Guid employeeId, CancellationToken cancellationToken)
    {
        if (period.Month == 1)
        {
            return 0m;
        }

        var priorPeriod = await periods.FindByYearMonthAsync(period.Year, period.Month - 1, cancellationToken);
        if (priorPeriod is null)
        {
            return 0m;
        }

        var priorPayslip = await payslips.FindByEmployeeAndPeriodAsync(employeeId, priorPeriod.Id, cancellationToken);
        return priorPayslip?.CumulativeGvBase ?? 0m;
    }
}
