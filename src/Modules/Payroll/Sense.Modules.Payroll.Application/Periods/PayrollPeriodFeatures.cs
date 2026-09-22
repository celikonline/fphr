using System.Globalization;
using System.Text.Json;
using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Application.Calculation;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Periods;

public sealed record PayrollPeriodDto(Guid Id, int Year, int Month, PayrollPeriodStatus Status);

/// <summary>
/// Read-only period list/detail (faz-07 tur 1: FE's `pages/payroll/periods.tsx`/`period-detail.tsx` reference these
/// two endpoints - not yet a PO-owned sözleşme, shape agreed with the FE twin's `payroll.service.ts` contract-first
/// note). Reuses payroll.trace.view (the module's existing "view payroll figures" permission), consistent with
/// <see cref="ExportPayrollPeriodQuery"/>'s gate - not payroll.period.recalculate, which is a write action.
/// </summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ListPayrollPeriodsQuery : IQuery<IReadOnlyList<PayrollPeriodDto>>;

public sealed class ListPayrollPeriodsHandler(IPayrollPeriodRepository periods) : IQueryHandler<ListPayrollPeriodsQuery, IReadOnlyList<PayrollPeriodDto>>
{
    public async Task<Result<IReadOnlyList<PayrollPeriodDto>>> Handle(ListPayrollPeriodsQuery query, CancellationToken cancellationToken)
    {
        var all = await periods.ListAllAsync(cancellationToken);
        return all.Select(p => new PayrollPeriodDto(p.Id, p.Year, p.Month, p.Status)).ToList();
    }
}

/// <summary>One employee's gross/net payslip line for the period-detail screen (no stored SGK/tax/stamp-duty
/// breakdown on <see cref="Payslip"/> itself - see <see cref="ExportPayrollPeriodQuery"/> for the full breakdown,
/// which reads the calculation trace instead). <paramref name="WorkedDays"/>/<paramref name="CalendarDaysInMonth"/>
/// (faz-07 tur 2 madde 6, G-7-11 eki, positional append - existing callers unaffected) come from the persisted
/// DAYS trace step (<see cref="PayslipTraceSteps.ExtractDays"/>); both null when the payslip has no trace yet.</summary>
public sealed record PayslipLineDto(Guid EmployeeId, string EmployeeName, decimal GrossAmount, decimal NetAmount, int? WorkedDays = null, int? CalendarDaysInMonth = null);

public sealed record PayrollPeriodDetailDto(Guid Id, int Year, int Month, PayrollPeriodStatus Status, IReadOnlyList<PayslipLineDto> Payslips);

[RequiresPermission("payroll.trace.view")]
public sealed record GetPayrollPeriodQuery(Guid PeriodId, Guid? CostCenterId = null) : IQuery<PayrollPeriodDetailDto>;

public sealed class GetPayrollPeriodHandler(
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IPayslipCalculationTraceRepository traces,
    IEmployeeDirectory employeeDirectory,
    IEmployeeCostCenterReader costCenterReader,
    ITenantContext tenant)
    : IQueryHandler<GetPayrollPeriodQuery, PayrollPeriodDetailDto>
{
    public async Task<Result<PayrollPeriodDetailDto>> Handle(GetPayrollPeriodQuery query, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(query.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var periodPayslips = await payslips.ListByPeriodAsync(period.Id, cancellationToken);

        // Filter by cost center if requested (faz-07 tur 1 md.1)
        if (query.CostCenterId.HasValue)
        {
            var costCenterMapping = await costCenterReader.GetEmployeeCostCenterMappingAsync(
                tenant.TenantId,
                periodPayslips.Select(p => p.EmployeeId).ToList(),
                cancellationToken);

            periodPayslips = periodPayslips
                .Where(p => costCenterMapping.TryGetValue(p.EmployeeId, out var cc) && cc == query.CostCenterId.Value)
                .ToList();
        }

        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, periodPayslips.Select(p => p.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);

        var lines = new List<PayslipLineDto>();
        foreach (var p in periodPayslips)
        {
            var contact = contacts.GetValueOrDefault(p.EmployeeId);
            var name = contact is null ? p.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
            var trace = await traces.FindLatestByPayslipIdAsync(p.Id, cancellationToken);
            var (workedDays, calendarDaysInMonth) = PayslipTraceSteps.ExtractDays(trace?.StepsJson);
            lines.Add(new PayslipLineDto(p.EmployeeId, name, p.GrossAmount, p.NetAmount, workedDays, calendarDaysInMonth));
        }

        return new PayrollPeriodDetailDto(period.Id, period.Year, period.Month, period.Status, lines);
    }
}

/// <summary>Opens a new tenant-month payroll period ("Dönem aç", 11.3.07 akış §2). Reuses payroll.period.recalculate
/// (no separate "open" permission key - opening is the first step of the same recalculation workflow).</summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record OpenPayrollPeriodCommand(int Year, int Month) : ICommand<Guid>;

public sealed class OpenPayrollPeriodValidator : AbstractValidator<OpenPayrollPeriodCommand>
{
    public OpenPayrollPeriodValidator()
    {
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Year).GreaterThan(2000);
    }
}

public sealed class OpenPayrollPeriodHandler(ITenantContext tenant, IPayrollPeriodRepository periods, TimeProvider clock) : ICommandHandler<OpenPayrollPeriodCommand, Guid>
{
    public async Task<Result<Guid>> Handle(OpenPayrollPeriodCommand command, CancellationToken cancellationToken)
    {
        // Statutory parameters (tax brackets, SGK ceilings) only exist for the seeded years: a period far outside the
        // window can never be calculated, so refuse it up front (400) instead of failing at run time.
        var maxYear = clock.GetUtcNow().Year + 1;
        if (command.Year < PayrollLimits.MinPeriodYear || command.Year > maxYear)
        {
            return Error.Validation(PayrollDomainErrors.PeriodYearOutOfRange, ("minYear", PayrollLimits.MinPeriodYear), ("maxYear", maxYear));
        }

        if (await periods.FindByYearMonthAsync(command.Year, command.Month, cancellationToken) is not null)
        {
            return Error.Conflict(PayrollDomainErrors.PeriodAlreadyOpen);
        }

        var created = PayrollPeriod.Create(tenant.TenantId, command.Year, command.Month);
        if (created.IsFailure)
        {
            return created.Error;
        }

        periods.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>
/// Recalculates every active employee's payslip in an Open period (11.3.07 akış §2 "hesapla" + §4 "Tümünü yeniden
/// hesapla" - the same operation, callable again after the initial run: see PayrollPeriodCalculationService doc).
/// Rejected on a non-Open period (Approved/Paid/Locked) - "Approved+ dönemde bayrak kalır, yeniden hesap yerine
/// Supplementary önerilir" (Supplementary runs are out of scope, P5+).
/// </summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record RecalculateAllPayslipsCommand(Guid PeriodId) : ICommand<PayrollPeriodRunSummary>;

public sealed class RecalculateAllPayslipsHandler(
    ITenantContext tenant, ICurrentUser currentUser, IPayrollPeriodRepository periods, IEmployeeDirectory employeeDirectory, PayrollPeriodCalculationService calculation)
    : ICommandHandler<RecalculateAllPayslipsCommand, PayrollPeriodRunSummary>
{
    public async Task<Result<PayrollPeriodRunSummary>> Handle(RecalculateAllPayslipsCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        if (period.Status != PayrollPeriodStatus.Open)
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotOpen);
        }

        // Active employees hired by period end + those terminated on/after the period start (exit-month payslip).
        var periodStart = new DateOnly(period.Year, period.Month, 1);
        var periodEnd = new DateOnly(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month));
        var employeeIds = await employeeDirectory.ListPayrollCandidateEmployeeIdsAsync(tenant.TenantId, periodStart, periodEnd, cancellationToken);
        try
        {
            var summary = await calculation.RecalculateAllAsync(period, employeeIds, cancellationToken);
            period.RecordCalculatedBy(currentUser.UserId);
            return summary;
        }
        catch (LegislationParameterMissingException ex)
        {
            return LegislationMissingError.From(ex);
        }
    }
}

/// <summary>One employee's figures in the deneme bordrosu, with the name.</summary>
public sealed record PayrollPreviewLineDto(Guid EmployeeId, string EmployeeName, decimal Gross, decimal Net, decimal EmployerCost);

/// <summary>Deneme bordrosu result: nothing is stored; totals cover only the calculated employees.</summary>
public sealed record PayrollPreviewDto(
    int CalculatedCount, int SkippedCount, decimal TotalGross, decimal TotalNet, decimal TotalEmployerCost,
    IReadOnlyList<PayrollPreviewLineDto> Lines, IReadOnlyList<PayrollSkippedEmployee> Skipped, IReadOnlyList<PayrollWarning> Warnings);

/// <summary>"Deneme bordrosu": runs the full calculation for an Open period without saving payslips, so figures can be reviewed first.</summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record PreviewPayrollPeriodQuery(Guid PeriodId) : IQuery<PayrollPreviewDto>;

public sealed class PreviewPayrollPeriodHandler(
    ITenantContext tenant, IPayrollPeriodRepository periods, IEmployeeDirectory employeeDirectory, PayrollPeriodCalculationService calculation)
    : IQueryHandler<PreviewPayrollPeriodQuery, PayrollPreviewDto>
{
    public async Task<Result<PayrollPreviewDto>> Handle(PreviewPayrollPeriodQuery query, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(query.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        if (period.Status != PayrollPeriodStatus.Open)
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotOpen);
        }

        var periodStart = new DateOnly(period.Year, period.Month, 1);
        var periodEnd = new DateOnly(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month));
        var employeeIds = await employeeDirectory.ListPayrollCandidateEmployeeIdsAsync(tenant.TenantId, periodStart, periodEnd, cancellationToken);
        PayrollPreviewResult result;
        try
        {
            result = await calculation.PreviewAsync(period, employeeIds, cancellationToken);
        }
        catch (LegislationParameterMissingException ex)
        {
            return LegislationMissingError.From(ex);
        }

        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, result.Lines.Select(l => l.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);
        IReadOnlyList<PayrollPreviewLineDto> lines = result.Lines
            .Select(l =>
            {
                var contact = contacts.GetValueOrDefault(l.EmployeeId);
                var name = contact is null ? l.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
                return new PayrollPreviewLineDto(l.EmployeeId, name, l.Gross, l.Net, l.EmployerCost);
            })
            .OrderBy(l => l.EmployeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new PayrollPreviewDto(
            result.Summary.CalculatedCount, result.Summary.SkippedCount,
            lines.Sum(l => l.Gross), lines.Sum(l => l.Net), lines.Sum(l => l.EmployerCost),
            lines, result.Summary.Skipped, result.Summary.Warnings);
    }
}

/// <summary>Recalculates only employees with an active dirty flag in this period ("Değişenleri yeniden hesapla", akış
/// §3). Same Open-period restriction as <see cref="RecalculateAllPayslipsCommand"/>.</summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record RecalculateDirtyPayslipsCommand(Guid PeriodId) : ICommand<PayrollPeriodRunSummary>;

public sealed class RecalculateDirtyPayslipsHandler(IPayrollPeriodRepository periods, PayrollPeriodCalculationService calculation, ICurrentUser currentUser)
    : ICommandHandler<RecalculateDirtyPayslipsCommand, PayrollPeriodRunSummary>
{
    public async Task<Result<PayrollPeriodRunSummary>> Handle(RecalculateDirtyPayslipsCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        if (period.Status != PayrollPeriodStatus.Open)
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotOpen);
        }

        try
        {
            var summary = await calculation.RecalculateDirtyAsync(period, cancellationToken);
            period.RecordCalculatedBy(currentUser.UserId);
            return summary;
        }
        catch (LegislationParameterMissingException ex)
        {
            return LegislationMissingError.From(ex);
        }
    }
}

/// <summary>One active "Değişti" flag, for the future "Değişenler" filter screen (P8).</summary>
public sealed record PayslipDirtyFlagDto(Guid Id, Guid EmployeeId, IReadOnlyList<string> Reasons, DateTime DetectedAt);

/// <summary>Every active dirty flag in a period (GET .../dirty-flags).</summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record ListDirtyFlagsQuery(Guid PeriodId) : IQuery<IReadOnlyList<PayslipDirtyFlagDto>>;

public sealed class ListDirtyFlagsHandler(IPayrollPeriodRepository periods, IPayslipDirtyFlagRepository dirtyFlags)
    : IQueryHandler<ListDirtyFlagsQuery, IReadOnlyList<PayslipDirtyFlagDto>>
{
    public async Task<Result<IReadOnlyList<PayslipDirtyFlagDto>>> Handle(ListDirtyFlagsQuery query, CancellationToken cancellationToken)
    {
        if (await periods.FindByIdAsync(query.PeriodId, cancellationToken) is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var flags = await dirtyFlags.ListActiveByPeriodAsync(query.PeriodId, cancellationToken);
        return flags.Select(f => new PayslipDirtyFlagDto(f.Id, f.EmployeeId, f.Reasons, f.DetectedAt)).ToList();
    }
}

/// <summary>
/// Bordro Excel özeti (K-7 PO kararı: SGK e-Bildirge/banka dosyası ilk sürüm dışı, dışa aktarım Faz 1'in
/// <see cref="IReportExporter"/>'ıyla bu özet - faz-07 tur 1 madde 4, S-14 sözleşmesi). Reuses payroll.trace.view
/// (the module's existing "view payroll figures" permission - no separate export permission key this slice).
/// Cross-tenant/unknown period both surface as 404 (<see cref="PayrollDomainErrors.PeriodNotFound"/>) since
/// <see cref="IPayrollPeriodRepository.FindByIdAsync"/> is already tenant-filtered.
/// </summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ExportPayrollPeriodQuery(Guid PeriodId, Guid? CostCenterId = null) : IQuery<ExportFileResult>;

public sealed class ExportPayrollPeriodHandler(
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IPayslipCalculationTraceRepository traces,
    IEmployeePayrollProfileRepository profiles,
    IEmployeeDirectory employeeDirectory,
    IExpensePayoutReader expensePayoutReader,
    IEmployeeCostCenterReader costCenterReader,
    ITenantContext tenant,
    IReportExporter exporter) : IQueryHandler<ExportPayrollPeriodQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(ExportPayrollPeriodQuery query, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(query.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var periodStart = new DateOnly(period.Year, period.Month, 1);
        var periodPayslips = await payslips.ListByPeriodAsync(period.Id, cancellationToken);

        // Filter by cost center if requested (faz-07 tur 1 md.1)
        if (query.CostCenterId.HasValue)
        {
            var costCenterMapping = await costCenterReader.GetEmployeeCostCenterMappingAsync(
                tenant.TenantId,
                periodPayslips.Select(p => p.EmployeeId).ToList(),
                cancellationToken);

            periodPayslips = periodPayslips
                .Where(p => costCenterMapping.TryGetValue(p.EmployeeId, out var cc) && cc == query.CostCenterId.Value)
                .ToList();
        }
        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, periodPayslips.Select(p => p.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);

        var rows = new List<(string Sicil, string AdSoyad, decimal Brut, decimal SgkIsci, decimal IssizlikIsci, decimal GelirVergisi, decimal Damga, decimal Net, decimal MasrafIadesi, decimal FissizMasraf)>();
        var missingSicilCount = 0;
        foreach (var payslip in periodPayslips)
        {
            var contact = contacts.GetValueOrDefault(payslip.EmployeeId);
            var adSoyad = contact is null ? payslip.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();

            // "Sicil" - Payroll has no dedicated employee-number field (Employee module doesn't either, verified this
            // tur); EmployeePayrollProfile.ExternalPayrollCode ("harici bordro kodu") is the closest existing concept
            // and is blank for most employees today (PO kararı, tur 1 kapanışı: resmi "sicil" bu alan, boşsa hücre
            // boş kalır + özet sayfasına sayısı yazılır - faz-07 tur 2 madde 1).
            var profile = await profiles.FindCurrentAsync(payslip.EmployeeId, periodStart, cancellationToken);
            var sicil = profile?.ExternalPayrollCode ?? string.Empty;
            if (sicil.Length == 0)
            {
                missingSicilCount++;
            }

            var trace = await traces.FindLatestByPayslipIdAsync(payslip.Id, cancellationToken);
            var (sgkEmployee, unempEmployee, incomeTaxAfterExemption, stampTaxAfterExemption, expenseReimbursement) = ExtractFigures(trace?.StepsJson);

            // S-13 eki (faz-07 tur 2 madde 5): fişsiz kalemler NET'e hiç girmez (ExtractFigures'daki expenseReimbursement
            // yalnız receipted kalemleri kapsar), Excel'de ayrı, bilgilendirici bir kolonda raporlanır.
            var payout = await expensePayoutReader.GetAsync(tenant.TenantId, payslip.EmployeeId, period.Year, period.Month, cancellationToken);
            var undocumentedExpense = payout?.UndocumentedExpenseTotal ?? 0m;

            rows.Add((sicil, adSoyad, payslip.GrossAmount, sgkEmployee, unempEmployee, incomeTaxAfterExemption, stampTaxAfterExemption, payslip.NetAmount, expenseReimbursement, undocumentedExpense));
        }

        var dataTable = new ExportTable(
            $"Bordro {period.Year}-{period.Month:D2}",
            [
                new ExportColumn("Sicil (Bordro Kodu)"),
                new ExportColumn("Ad Soyad"),
                new ExportColumn("Brüt", ExportColumnType.Number),
                new ExportColumn("SGK İşçi", ExportColumnType.Number),
                new ExportColumn("İşsizlik Sigortası İşçi", ExportColumnType.Number),
                new ExportColumn("Gelir Vergisi", ExportColumnType.Number),
                new ExportColumn("Damga Vergisi", ExportColumnType.Number),
                new ExportColumn("Net", ExportColumnType.Number),
                new ExportColumn("Masraf İadesi", ExportColumnType.Number),
                new ExportColumn("Fişsiz Masraf (Bordroya Eklenmedi)", ExportColumnType.Number),
            ],
            rows.OrderBy(r => r.AdSoyad, StringComparer.Create(new CultureInfo("tr-TR"), ignoreCase: false))
                .Select(r => (IReadOnlyList<object?>)[r.Sicil, r.AdSoyad, r.Brut, r.SgkIsci, r.IssizlikIsci, r.GelirVergisi, r.Damga, r.Net, r.MasrafIadesi, r.FissizMasraf])
                .ToList());

        // Özet sayfası (faz-07 tur 2 madde 1): "Bordro kodu tanımsız çalışan: N".
        var summaryTable = new ExportTable(
            "Özet",
            [
                new ExportColumn("Metrik"),
                new ExportColumn("Değer", ExportColumnType.Number),
            ],
            [
                [(object?)"Toplam çalışan", rows.Count],
                [(object?)"Bordro kodu tanımsız çalışan", missingSicilCount],
            ]);

        return exporter.ExportToExcel($"Bordro {period.Year}-{period.Month:D2}.xlsx", [dataTable, summaryTable], cancellationToken);
    }

    /// <summary>Re-derives the after-exemption income/stamp tax and the expense reimbursement NET addition from the
    /// stored trace JSON (see PayrollCalculator: MINWAGE_EXEMPTION_GV/DV steps' Output is the exemption *amount*, not
    /// the after-exemption tax, and NET's expenseReimbursement input is the tax-free masraf iadesi - TB-7).
    /// UNEMP_EMPLOYEE (G-7-10, faz-07 tur 2 madde 2) is read straight from its own step, no exemption to net out.</summary>
    private static (decimal SgkEmployee, decimal UnempEmployee, decimal IncomeTaxAfterExemption, decimal StampTaxAfterExemption, decimal ExpenseReimbursement) ExtractFigures(string? stepsJson)
    {
        if (string.IsNullOrEmpty(stepsJson))
        {
            return (0m, 0m, 0m, 0m, 0m);
        }

        var steps = PayslipTraceSteps.Parse(stepsJson);
        var sgkEmployee = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.SgkEmployee)?.Output ?? 0m;
        var unempEmployee = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.UnempEmployee)?.Output ?? 0m;
        var incomeTax = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.IncomeTax)?.Output ?? 0m;
        var minWageExemptionGv = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.MinWageExemptionGv)?.Output ?? 0m;
        var stampTax = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.StampTax)?.Output ?? 0m;
        var minWageExemptionDv = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.MinWageExemptionDv)?.Output ?? 0m;
        var netStep = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.Net);
        var expenseReimbursement = netStep is not null && netStep.Inputs.TryGetValue("expenseReimbursement", out var value)
            ? decimal.Parse(value, CultureInfo.InvariantCulture)
            : 0m;

        return (sgkEmployee, unempEmployee, incomeTax - minWageExemptionGv, stampTax - minWageExemptionDv, expenseReimbursement);
    }
}

/// <summary>Shared <see cref="PayslipCalculationTrace.StepsJson"/> parsing (faz-07 tur 2 madde 6): both
/// <see cref="ExportPayrollPeriodHandler"/>'s figure extraction and <see cref="GetPayrollPeriodHandler"/>'s day-count
/// read from the same blob - kept in one place so the JSON options/step lookup stay in sync.</summary>
internal static class PayslipTraceSteps
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static List<TraceStep> Parse(string? stepsJson) =>
        string.IsNullOrEmpty(stepsJson) ? [] : JsonSerializer.Deserialize<List<TraceStep>>(stepsJson, Options) ?? [];

    /// <summary>Reads the DAYS step's <c>workedDays</c>/<c>calendarDaysInMonth</c> inputs (both ints, see
    /// <see cref="PayrollCalculator.Calculate"/>). Null when the payslip has no trace, no DAYS step, or the input is
    /// missing/unparsable - callers surface that as "unknown", not zero.</summary>
    public static (int? WorkedDays, int? CalendarDaysInMonth) ExtractDays(string? stepsJson)
    {
        var daysStep = Parse(stepsJson).FirstOrDefault(s => s.Code == PayrollTraceStepCodes.Days);
        if (daysStep is null)
        {
            return (null, null);
        }

        return (ParseInt(daysStep, "workedDays"), ParseInt(daysStep, "calendarDaysInMonth"));
    }

    private static int? ParseInt(TraceStep step, string key) =>
        step.Inputs.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
