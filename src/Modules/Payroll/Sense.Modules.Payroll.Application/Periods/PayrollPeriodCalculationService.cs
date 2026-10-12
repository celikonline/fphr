using Microsoft.Extensions.Logging;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Payroll.Application.Calculation;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Garnishments;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Payroll.Domain.Unions;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>Why an employee got no payslip in a run (see <see cref="PayrollSkipReasons"/>).</summary>
public sealed record PayrollSkippedEmployee(Guid EmployeeId, string Reason);

/// <summary>Machine-readable reasons an employee is skipped by <see cref="PayrollPeriodCalculationService"/>.</summary>
public static class PayrollSkipReasons
{
    public const string NoPayrollProfile = "no_payroll_profile";
    public const string WageBasisNotMonthly = "wage_basis_not_monthly";
    public const string WageBasisMismatch = "wage_basis_mismatch";
    public const string NetWageBasisUnsupported = "net_wage_basis_unsupported";
    public const string NoSalaryRecord = "no_salary_record";
    public const string NotEmployedInPeriod = "not_employed_in_period";
    public const string NoWorkedDays = "no_worked_days";
    public const string CalculationFailed = "calculation_failed";

    /// <summary>A pay item's formula could not be evaluated for this employee (division by zero, overflow, ...); the log names only the pay item code.</summary>
    public const string PayItemFormulaFailed = "pay_item_formula_failed";
}

/// <summary>senseik-019 2a: non-blocking warning from payroll run (e.g. cumulative GV_BASE unavailable for mid-year employee).</summary>
public sealed record PayrollWarning(Guid EmployeeId, string Code, string? Details = null);

/// <summary>Warning codes added by the pay-item (formüllü ödeme) integration.</summary>
public static class PayrollWarningCodes
{
    /// <summary>A pay item's formula came out negative and was set to 0; <see cref="PayrollWarning.Details"/> is the pay item code.</summary>
    public const string PayItemNegativeResult = "pay_item_negative_result";
}

/// <summary>Outcome of one <see cref="PayrollPeriodCalculationService"/> run. Employees are skipped (not counted in
/// <see cref="CalculatedCount"/>) when they have no payroll profile/salary record, a non-Monthly WageBasis, a Net
/// SalaryType, were not employed in the period, have no worked day, or their calculation failed; each skipped
/// employee is listed in <see cref="Skipped"/> with a machine-readable <see cref="PayrollSkipReasons"/> code.
/// <see cref="Warnings"/> contains non-blocking issues like missing prior cumulative GV base for mid-year employees.</summary>
/// <summary>One employee's figures in a deneme bordrosu.</summary>
public sealed record PayrollPreviewLine(Guid EmployeeId, decimal Gross, decimal Net, decimal EmployerCost);

/// <summary>A deneme bordrosu: the run summary (calculated / skipped / warnings) plus every calculated employee's figures.</summary>
public sealed record PayrollPreviewResult(PayrollPeriodRunSummary Summary, IReadOnlyList<PayrollPreviewLine> Lines);

public sealed record PayrollPeriodRunSummary(
    Guid RunId, int CalculatedCount, int SkippedCount, IReadOnlyList<PayrollSkippedEmployee> Skipped,
    IReadOnlyList<PayrollWarning> Warnings = default!)
{
    public IReadOnlyList<PayrollWarning> Warnings { get; } = Warnings ?? [];
}

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
    IEmployeeDisabilityLookup disabilityLookup,
    IEmployeeUnionMembershipRepository unionMemberships,
    IOvertimePayrollReader overtimeReader,
    IPayrollAdjustmentRepository adjustmentRepository,
    IEmployeePayItemAssignmentRepository payItemAssignments,
    IPayItemDefinitionRepository payItemDefinitions,
    ILogger<PayrollPeriodCalculationService> logger)
{
    private static readonly System.Text.Json.JsonSerializerOptions StepsJsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>Recalculates every candidate employee (initial "Dönem aç → hesapla" run, and later "Tümünü yeniden hesapla").</summary>
    public Task<PayrollPeriodRunSummary> RecalculateAllAsync(PayrollPeriod period, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken) =>
        RunAsync(period, employeeIds, null, cancellationToken);

    /// <summary>
    /// Deneme bordrosu: runs the same calculation for every candidate employee but writes nothing - no payslip, trace, flag clearing or
    /// garnishment balance - and returns the figures so they can be reviewed before the real run.
    /// </summary>
    public async Task<PayrollPreviewResult> PreviewAsync(PayrollPeriod period, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken)
    {
        var lines = new List<PayrollPreviewLine>();
        var summary = await RunAsync(period, employeeIds, lines, cancellationToken);
        return new PayrollPreviewResult(summary, lines);
    }

    private async Task<PayrollPeriodRunSummary> RunAsync(
        PayrollPeriod period, IReadOnlyCollection<Guid> employeeIds, List<PayrollPreviewLine>? preview, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var periodStart = new DateOnly(period.Year, period.Month, 1);
        var calendarDays = DateTime.DaysInMonth(period.Year, period.Month);
        var snapshot = await PayrollLegislationSnapshotResolver.ResolveAsync(legislationReader, periodStart, cancellationToken);
        var payItemPlan = await LoadPayItemPlanAsync(periodStart, cancellationToken);

        var calculated = 0;
        var skipped = new List<PayrollSkippedEmployee>();
        var warnings = new List<PayrollWarning>();
        foreach (var employeeId in employeeIds)
        {
            string? skipReason;
            try
            {
                skipReason = await CalculateOneAsync(period, employeeId, periodStart, calendarDays, snapshot, runId, warnings, preview, payItemPlan, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One employee's bad data must not break the whole period run: report it as skipped and carry on.
                logger.LogError(ex, "Payroll calculation failed for employee {EmployeeId} in period {Year}-{Month}; employee skipped.", employeeId, period.Year, period.Month);
                skipReason = PayrollSkipReasons.CalculationFailed;
            }

            if (skipReason is null)
            {
                calculated++;
            }
            else
            {
                skipped.Add(new PayrollSkippedEmployee(employeeId, skipReason));
            }
        }

        return new PayrollPeriodRunSummary(runId, calculated, skipped.Count, skipped, warnings);
    }

    /// <summary>Recalculates only the employees with an active dirty flag in this period ("Değişenleri yeniden hesapla", §4);
    /// clears each flag on a successful calculation. Employees whose flag cannot be resolved (no profile/salary) keep their flag.</summary>
    public async Task<PayrollPeriodRunSummary> RecalculateDirtyAsync(PayrollPeriod period, CancellationToken cancellationToken)
    {
        var activeFlags = await dirtyFlags.ListActiveByPeriodAsync(period.Id, cancellationToken);
        return await RecalculateAllAsync(period, activeFlags.Select(f => f.EmployeeId).Distinct().ToList(), cancellationToken);
    }

    /// <summary>Calculates one employee; returns null on success, otherwise the <see cref="PayrollSkipReasons"/> code. Accumulates warnings in the provided list.</summary>
    private async Task<string?> CalculateOneAsync(
        PayrollPeriod period, Guid employeeId, DateOnly periodStart, int calendarDays, PayrollLegislationSnapshot snapshot, Guid runId, List<PayrollWarning> warnings, List<PayrollPreviewLine>? preview,
        IReadOnlyDictionary<Guid, List<(EmployeePayItemAssignment Assignment, PayItemDefinition Definition)>> payItemPlan, CancellationToken cancellationToken)
    {
        var periodEnd = new DateOnly(period.Year, period.Month, calendarDays);

        // Kıst (işe giriş/çıkış ayı): dates unknown (null) = assume employed the whole period, the pre-existing behaviour.
        var employment = await salaryLookup.GetEmploymentDatesAsync(tenant.TenantId, employeeId, cancellationToken);
        var paidDays30 = EmploymentProration.PaidDays(periodStart, periodEnd, employment?.HireDate, employment?.TerminationDate);
        if (paidDays30 == 0)
        {
            return PayrollSkipReasons.NotEmployedInPeriod;
        }

        var isPartialMonth = paidDays30 < EmploymentProration.PayrollMonthDays;

        // A profile created on hire is valid from the hire date, which for a mid-month hire is after the period start.
        var profile = await profiles.FindCurrentAsync(employeeId, EmploymentProration.EffectiveStart(periodStart, employment?.HireDate), cancellationToken);
        if (profile is null)
        {
            return PayrollSkipReasons.NoPayrollProfile; // no payroll profile covering this period yet
        }

        // Monthly, Daily and Hourly are calculated; Weekly and Yearly are still out of scope.
        if (profile.WageBasis != WageBasis.Monthly && !PeriodWageMath.IsSupported(profile.WageBasis))
        {
            return PayrollSkipReasons.WageBasisNotMonthly;
        }

        // The salary in force at the period's end, not today's (a later raise must not rewrite a closed month).
        var salary = await salaryLookup.GetSalaryAsOfAsync(tenant.TenantId, employeeId, periodEnd, cancellationToken);
        if (salary is null)
        {
            return PayrollSkipReasons.NoSalaryRecord; // no salary record in force yet
        }

        // The salary record's pay frequency says what the amount means (monthly gross, daily gross or hourly gross); it must agree with the profile.
        if (!WageBasisMatchesPayFrequency(profile.WageBasis, salary.PayFrequency))
        {
            return PayrollSkipReasons.WageBasisMismatch;
        }

        var isPeriodWage = profile.WageBasis != WageBasis.Monthly;
        if (isPeriodWage && salary.SalaryType == EmployeeSalaryType.Net)
        {
            // BASİTLEŞTİRME: net günlük/saatlik ücretin net→brüt çözümü (aylık NetToGrossSolver'a eşdeğer) yapılmıyor; sessizce yanlış hesap yerine atlanır.
            return PayrollSkipReasons.NetWageBasisUnsupported;
        }

        // Cross-month cumulative GV_BASE tracking (Ocak'tan itibaren kümülatif matrah) senseik-019 2a ile devreye
        // girdi - bkz. ResolvePriorCumulativeGvBaseAsync (bu metodun altında).
        // TB-7 Attendance köprüsü (roadmap #23): null (no adapter wired yet, or Attendance has no data for this
        // employee/period) keeps today's full-month assumption; once IWorkedTimeReader has a real Attendance-backed
        // implementation, WorkedDays reflects actual attendance instead.
        // Kıst ayında PDKS yalnız istihdam penceresine bakar: işe girişten önceki / çıkıştan sonraki iş günleri "eksik gün" sayılıp
        // paidDays30'dan ikinci kez düşülmesin. Tam ayda pencere verilmez (aylık çalışanın davranışı aynı kalır).
        var workedTime = isPartialMonth
            ? await workedTimeReader.GetAsync(
                tenant.TenantId, employeeId, period.Year, period.Month,
                EmploymentProration.EffectiveStart(periodStart, employment?.HireDate), EmploymentProration.EffectiveEnd(periodEnd, employment?.TerminationDate), cancellationToken)
            : await workedTimeReader.GetAsync(tenant.TenantId, employeeId, period.Year, period.Month, cancellationToken: cancellationToken);

        int workedDays;
        int missingDays;
        if (workedTime is not null)
        {
            // Attendance (PDKS) already resolved a worked-day figure - use it as-is; the Leave absence bridge below
            // is skipped entirely to avoid double-counting the same missing days from two sources (G-7-08/09, faz-07
            // tur 2 madde 3 "Çift sayım kuralı").
            workedDays = workedTime.WorkedDays;
            missingDays = workedTime.MissingDays;
        }
        else
        {
            // No Attendance data this employee/period: fall back to İK's manually entered devamsızlık (Leave
            // AbsenceRecord, Unexcused only) as the missing-day source. Both null (no Attendance, no absence data
            // either) keeps the pre-existing full-calendar-month assumption; a null or zero absence count leaves
            // workedDays unchanged (G-7-08 PO kararı: WorkedDays bu turda yalnız bugünkü kullanım - yemek
            // pro-rasyonu - için, hafta tatili/resmi tatil ücret hakkına dokunmaz).
            var unexcusedDays = await absenceDaysReader.GetUnexcusedDaysAsync(tenant.TenantId, employeeId, period.Year, period.Month, cancellationToken);
            missingDays = unexcusedDays is > 0 ? unexcusedDays.Value : 0;
            workedDays = unexcusedDays is > 0 ? Math.Max(0, calendarDays - unexcusedDays.Value) : calendarDays;
        }

        if (isPartialMonth)
        {
            workedDays = Math.Min(workedDays, paidDays30);
        }

        if (workedDays <= 0)
        {
            // e.g. Attendance has a check-in but no worked-minutes day: PayrollCalculator requires >= 1 worked day.
            logger.LogWarning("Payroll skipped employee {EmployeeId} in period {Year}-{Month}: no worked days.", employeeId, period.Year, period.Month);
            return PayrollSkipReasons.NoWorkedDays;
        }

        // Günlük/saatlik ücret: ücretli gün = 30 esaslı kısmi ay günü eksi eksik gün (Attendance varsa MissingDays, yoksa Leave'in mazeretsiz devamsızlığı); 0'ın altına inmez.
        var wagePaidDays = Math.Max(0, paidDays30 - missingDays);
        if (isPeriodWage && wagePaidDays <= 0)
        {
            logger.LogWarning("Payroll skipped employee {EmployeeId} in period {Year}-{Month}: no paid days.", employeeId, period.Year, period.Month);
            return PayrollSkipReasons.NoWorkedDays;
        }

        // TB-7 Expense köprüsü (roadmap #23, faz-07 tur 1 madde 3): port boş/null dönerse 0 - bugünkü davranış sürer.
        var expensePayout = await expensePayoutReader.GetAsync(tenant.TenantId, employeeId, period.Year, period.Month, cancellationToken);
        var expenseReimbursement = expensePayout?.ApprovedExpenseTotal ?? 0m;

        // senseik-019 2a: kümülatif GV matrahı devri - önceki dönemin Payslip'inden okunur (yıl başında veya önceki
        // dönem/payslip yoksa 0'dan başlanır, örn. yıl içinde işe giren çalışan).
        var priorCumulativeGvBase = await ResolvePriorCumulativeGvBaseAsync(period, employeeId, cancellationToken);
        if (priorCumulativeGvBase == 0 && period.Month > 1)
        {
            logger.LogWarning("Payroll period {Year}-{Month}: Employee {EmployeeId} has no prior cumulative GV base; tax bracket may be underreported.", period.Year, period.Month, employeeId);
            warnings.Add(new PayrollWarning(employeeId, "missing_prior_cumulative_gv_base", $"Month {period.Month}: no prior payslip found"));
        }

        // senseik-019 2a: BASİTLEŞTİRME - gerçek teşvik uygunluk kontrolü (SGK'nın güncel kanun kodu listesiyle
        // eşleşme, işyeri bazlı başvuru durumu) bu sürümde yapılmıyor, bkz. PayrollCalculationInput.IncentiveEligible.
        var incentiveEligible = !string.IsNullOrWhiteSpace(profile.IncentiveLawCode);

        // senseik-019 2b: GVK md.31 engelli çalışan gelir vergisi matrah indirimi derecesi.
        var disabilityDegree = await disabilityLookup.GetDegreeAsync(tenant.TenantId, employeeId, cancellationToken) ?? EmployeeDisabilityDegree.None;

        // Net ücret (net→brüt): a fixed-net salary is grossed up with the same calculator so the employee keeps exactly that net after the
        // month's premiums and taxes (cumulative bracket, exemptions and disability reduction included). Solved on a full month with no
        // additions or deductions, then the normal run prorates that gross for a partial month and adds meal/overtime/deductions as usual.
        // Günlük/saatlik ücretlide grossMonthly aylık karşılıktır (günlük x 30, saatlik x 225): fazla mesai, tatil ücreti ve sendika aidatı onunla hesaplanır; dönem brütü PeriodWage ile bulunur.
        var periodWage = isPeriodWage ? new PeriodWage(profile.WageBasis, salary.Amount, wagePaidDays) : null;
        var grossMonthly = isPeriodWage
            ? PeriodWageMath.MonthlyEquivalent(profile.WageBasis, salary.Amount)
            : salary.SalaryType == EmployeeSalaryType.Net
            ? NetToGrossSolver.Solve(
                salary.Amount,
                candidateGross => PayrollCalculator.Calculate(
                    new PayrollCalculationInput(
                        GrossMonthlySalary: candidateGross, WorkedDays: calendarDays, CalendarDaysInMonth: calendarDays,
                        MealVariant: MealBenefitVariant.None, MealDailyAmount: 0m, PriorCumulativeGvBase: priorCumulativeGvBase,
                        AsOfDate: periodStart, DisabilityDegree: (int)disabilityDegree),
                    snapshot))
            : salary.Amount;

        // senseik-019 2c: açık avans bakiyesi + aktif icra/nafaka emirleri.
        var advanceBalance = expensePayout?.OpenAdvanceBalance ?? 0m;
        var activeGarnishments = await garnishmentOrderRepository.ListActiveByEmployeeAsync(employeeId, cancellationToken) ?? [];
        var garnishmentInputs = activeGarnishments
            .Select(o => new GarnishmentDeductionInput(o.Id, o.Type, o.MonthlyInstallmentAmount, o.RemainingBalance))
            .ToList();

        // Sendika / dayanışma aidatı (11.3.07-bordro.md #58): pure UnionDuesCalculator decides eligibility for this
        // period from the current membership (or 0 if the employee has none) - the calculator never touches the DB.
        var membership = await unionMemberships.FindCurrentAsync(employeeId, periodStart, cancellationToken);
        var unionDuesAmount = UnionDuesCalculator.Calculate(membership, grossMonthly, calendarDays, periodStart);

        // Fazla mesai (4857 md.41): Leave's approved overtime minutes (25%/50% rates, time-off conversions excluded) become a taxable
        // earning added to the gross. Leave returns nothing for an employee without overtime, which leaves the gross unchanged.
        var overtime = (await overtimeReader.GetAsync(tenant.TenantId, period.Year, period.Month, [employeeId], cancellationToken))?.FirstOrDefault(o => o.EmployeeId == employeeId);
        var overtimePay = overtime is null
            ? 0m
            : OvertimePayCalculator.Calculate(grossMonthly, overtime.Rate25Minutes / 60m, overtime.Rate50Minutes / 60m);

        // Prim, komisyon ve benzeri tek seferlik brüt ek kazançlar (Open dönemde girilir): fazla mesai gibi vergiye ve SGK ya tabi.
        var monthAdjustments = (await adjustmentRepository.ListByEmployeeAndMonthAsync(employeeId, period.Year, period.Month, cancellationToken)) ?? [];
        var adjustmentsTotal = monthAdjustments.Where(a => !a.IsNet).Sum(a => a.GrossAmount);
        var netAdjustmentsTotal = monthAdjustments.Where(a => a.IsNet).Sum(a => a.GrossAmount);

        // Hafta tatili / resmî tatilde çalışma (4857 md.46-47): aylık ücretli için tatil günü ücrete dahildir, çalışılan her tatil günü +1 günlük ücret ek kazançtır.
        var holidayPay = workedTime is { PublicHolidayDaysWorked: > 0 } or { WeeklyRestDaysWorked: > 0 }
            ? HolidayPayCalculator.Calculate(grossMonthly, calendarDays, workedTime.WeeklyRestDaysWorked, workedTime.PublicHolidayDaysWorked)
            : 0m;

        // Nakit yol yardımı: ayni (kart, servis) çalışana para ödemez; nakit olan çalıştığı gün kadar vergiye ve SGK ya tabi ek kazançtır.
        var transportPay = profile.TransportVariant == TransportVariant.Cash ? profile.TransportDailyAmount * workedDays : 0m;

        PayrollCalculationInput BuildInput(decimal extraGross, IReadOnlyList<PayItemEarning>? payItemEarnings = null) => new(
            GrossMonthlySalary: grossMonthly,
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
            GarnishmentOrders: garnishmentInputs,
            UnionDuesAmount: unionDuesAmount,
            AdditionalTaxableEarnings: overtimePay + adjustmentsTotal + transportPay + holidayPay + extraGross,
            ProratedPaidDays: ProratedDays(),
            Wage: periodWage,
            PayItems: payItemEarnings is { Count: > 0 } ? payItemEarnings : null);

        // SGK taban/tavan ve asgari ücret istisnaları kısmi ayda kıst gün, günlük/saatlikte eksik gün düşülmüş ücretli gün kadar ölçeklenir.
        int? ProratedDays()
        {
            var scaledDays = isPeriodWage ? wagePaidDays : paidDays30;
            return scaledDays < EmploymentProration.PayrollMonthDays ? scaledDays : null;
        }

        // Formüllü ödeme türleri (PAY-FORMULA-3): değişkenler kalemler eklenmeden önceki girdiden okunur; atama yoksa bu blok hiç çalışmaz.
        IReadOnlyList<EvaluatedPayItem> evaluatedItems = [];
        if (payItemPlan.TryGetValue(employeeId, out var assignedItems) && assignedItems.Count > 0)
        {
            var baseInput = BuildInput(0m);
            var facts = new PayItemVariableFacts(
                NetTarget: !isPeriodWage && salary.SalaryType == EmployeeSalaryType.Net ? salary.Amount : 0m,
                MissingDays: missingDays,
                SeniorityYears: PayItemVariables.SeniorityYears(employment?.HireDate, periodEnd),
                OvertimeHours: overtime is null ? 0m : (overtime.Rate25Minutes + overtime.Rate50Minutes) / 60m);
            var evaluation = PayItemEvaluator.Evaluate(assignedItems, deger => PayItemVariables.Build(baseInput, snapshot, facts, deger));
            if (!evaluation.Succeeded)
            {
                // Tutar ve formül metni loga yazılmaz; yalnız ödeme türü kodu.
                logger.LogWarning("Payroll skipped employee {EmployeeId} in period {Year}-{Month}: formula of pay item {PayItemCode} failed.", employeeId, period.Year, period.Month, evaluation.FailedCode);
                return PayrollSkipReasons.PayItemFormulaFailed;
            }

            warnings.AddRange(evaluation.NegativeCodes.Select(code => new PayrollWarning(employeeId, PayrollWarningCodes.PayItemNegativeResult, code)));
            evaluatedItems = evaluation.Items;
        }

        // Brüt kalemler olduğu gibi girer; net kalemler (yalnız tam vergiye tabi) net primlerle TEK çözümde brütleştirilir.
        var grossEarnings = evaluatedItems.Where(i => !i.IsNet).Select(i => ToEarning(i, i.Amount, null)).ToList();
        var netItems = evaluatedItems.Where(i => i.IsNet).ToList();
        var netExtraTotal = netAdjustmentsTotal + netItems.Sum(i => i.Amount);

        // Net prim: brütleştirilir - çalışanın eline tam girilen net geçecek şekilde, ayın diğer kazançları ve dilim/kümülatif matrahla birlikte.
        var netBonusGross = netExtraTotal > 0m
            ? NetToGrossSolver.SolveExtra(netExtraTotal, extra => PayrollCalculator.Calculate(BuildInput(extra, grossEarnings), snapshot))
            : 0m;
        var adjustmentGross = netBonusGross;
        IReadOnlyList<decimal> netItemGross = [];
        if (netItems.Count > 0)
        {
            (adjustmentGross, netItemGross) = PayItemNetAllocator.Allocate(netBonusGross, netAdjustmentsTotal, netItems.Select(i => i.Amount).ToList());
        }

        var payItemEarnings = new List<PayItemEarning>();
        var netIndex = 0;
        foreach (var item in evaluatedItems)
        {
            payItemEarnings.Add(item.IsNet ? ToEarning(item, netItemGross[netIndex++], item.Amount) : ToEarning(item, item.Amount, null));
        }

        var input = BuildInput(adjustmentGross, payItemEarnings);

        var result = PayrollCalculator.Calculate(input, snapshot);

        if (preview is not null)
        {
            // Deneme bordrosu: report the figures and stop before anything is persisted or mutated (garnishment balances, flags, traces).
            preview.Add(new PayrollPreviewLine(employeeId, result.GrossAmount, result.NetAmount, result.EmployerCost));
            return null;
        }

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

        return null;
    }

    /// <summary>
    /// Loads, once per run, the pay item assignments in force in the month (assignment range covers the month, definition active and
    /// date-overlapping) grouped by employee. Passive definitions and out-of-range assignments never reach the calculation.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, List<(EmployeePayItemAssignment Assignment, PayItemDefinition Definition)>>> LoadPayItemPlanAsync(
        DateOnly periodStart, CancellationToken cancellationToken)
    {
        var plan = new Dictionary<Guid, List<(EmployeePayItemAssignment, PayItemDefinition)>>();
        var covering = (await payItemAssignments.ListCoveringMonthAsync(periodStart, cancellationToken))?.Where(a => a.IsCovered(periodStart)).ToList() ?? [];
        if (covering.Count == 0)
        {
            return plan;
        }

        var definitions = ((await payItemDefinitions.ListByIdsAsync(covering.Select(a => a.PayItemDefinitionId).Distinct().ToList(), cancellationToken)) ?? [])
            .ToDictionary(d => d.Id);
        foreach (var assignment in covering)
        {
            if (!definitions.TryGetValue(assignment.PayItemDefinitionId, out var definition) || !definition.IsEffectiveInMonth(periodStart))
            {
                continue;
            }

            if (!plan.TryGetValue(assignment.EmployeeId, out var list))
            {
                list = [];
                plan[assignment.EmployeeId] = list;
            }

            list.Add((assignment, definition));
        }

        return plan;
    }

    private static PayItemEarning ToEarning(EvaluatedPayItem item, decimal grossAmount, decimal? netTarget) => new(
        item.Code, item.Name, grossAmount, item.SgkSubject, item.IncomeTaxSubject, item.StampTaxSubject, item.Formula, item.UsedVariables, netTarget);

    private static bool WageBasisMatchesPayFrequency(WageBasis basis, EmployeePayFrequency frequency) => basis switch
    {
        WageBasis.Monthly => frequency == EmployeePayFrequency.Monthly,
        WageBasis.Daily => frequency == EmployeePayFrequency.Daily,
        WageBasis.Hourly => frequency == EmployeePayFrequency.Hourly,
        _ => false,
    };

    /// <summary>senseik-019 2a: Ocak'ta (yeni takvim yılı) her zaman 0'dan başlanır. Aksi halde bu çalışanın AYNI YILDAKİ
    /// en yakın önceki dönem Payslip'inin CUMULATIVE_BASE çıktısı devralınır (ara bir ay açılmamış/çalışanın o ayda
    /// payslip'i yoksa daha geriye bakılır); hiç önceki payslip yoksa (örn. yıl içinde işe giren çalışan) 0 kabul edilir.</summary>
    private async Task<decimal> ResolvePriorCumulativeGvBaseAsync(PayrollPeriod period, Guid employeeId, CancellationToken cancellationToken)
    {
        for (var month = period.Month - 1; month >= 1; month--)
        {
            var priorPeriod = await periods.FindByYearMonthAsync(period.Year, month, cancellationToken);
            if (priorPeriod is null)
            {
                continue;
            }

            var priorPayslip = await payslips.FindByEmployeeAndPeriodAsync(employeeId, priorPeriod.Id, cancellationToken);
            if (priorPayslip is not null)
            {
                return priorPayslip.CumulativeGvBase;
            }
        }

        return 0m;
    }
}
