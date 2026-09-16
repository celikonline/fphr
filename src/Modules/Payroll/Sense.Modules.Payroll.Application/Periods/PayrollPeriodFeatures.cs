using System.Globalization;
using System.Text.Json;
using FluentValidation;
using Sense.Modules.Employee.Contracts;
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
/// which reads the calculation trace instead).</summary>
public sealed record PayslipLineDto(Guid EmployeeId, string EmployeeName, decimal GrossAmount, decimal NetAmount);

public sealed record PayrollPeriodDetailDto(Guid Id, int Year, int Month, PayrollPeriodStatus Status, IReadOnlyList<PayslipLineDto> Payslips);

[RequiresPermission("payroll.trace.view")]
public sealed record GetPayrollPeriodQuery(Guid PeriodId) : IQuery<PayrollPeriodDetailDto>;

public sealed class GetPayrollPeriodHandler(IPayrollPeriodRepository periods, IPayslipRepository payslips, IEmployeeDirectory employeeDirectory, ITenantContext tenant)
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
        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, periodPayslips.Select(p => p.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);

        var lines = periodPayslips.Select(p =>
        {
            var contact = contacts.GetValueOrDefault(p.EmployeeId);
            var name = contact is null ? p.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
            return new PayslipLineDto(p.EmployeeId, name, p.GrossAmount, p.NetAmount);
        }).ToList();

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

public sealed class OpenPayrollPeriodHandler(ITenantContext tenant, IPayrollPeriodRepository periods) : ICommandHandler<OpenPayrollPeriodCommand, Guid>
{
    public async Task<Result<Guid>> Handle(OpenPayrollPeriodCommand command, CancellationToken cancellationToken)
    {
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
    ITenantContext tenant, IPayrollPeriodRepository periods, IEmployeeDirectory employeeDirectory, PayrollPeriodCalculationService calculation)
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

        var employeeIds = await employeeDirectory.ListActiveEmployeeIdsAsync(tenant.TenantId, cancellationToken);
        return await calculation.RecalculateAllAsync(period, employeeIds, cancellationToken);
    }
}

/// <summary>Recalculates only employees with an active dirty flag in this period ("Değişenleri yeniden hesapla", akış
/// §3). Same Open-period restriction as <see cref="RecalculateAllPayslipsCommand"/>.</summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record RecalculateDirtyPayslipsCommand(Guid PeriodId) : ICommand<PayrollPeriodRunSummary>;

public sealed class RecalculateDirtyPayslipsHandler(IPayrollPeriodRepository periods, PayrollPeriodCalculationService calculation)
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

        return await calculation.RecalculateDirtyAsync(period, cancellationToken);
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
public sealed record ExportPayrollPeriodQuery(Guid PeriodId) : IQuery<ExportFileResult>;

public sealed class ExportPayrollPeriodHandler(
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IPayslipCalculationTraceRepository traces,
    IEmployeePayrollProfileRepository profiles,
    IEmployeeDirectory employeeDirectory,
    ITenantContext tenant,
    IReportExporter exporter) : IQueryHandler<ExportPayrollPeriodQuery, ExportFileResult>
{
    private static readonly JsonSerializerOptions StepsJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<ExportFileResult>> Handle(ExportPayrollPeriodQuery query, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(query.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var periodStart = new DateOnly(period.Year, period.Month, 1);
        var periodPayslips = await payslips.ListByPeriodAsync(period.Id, cancellationToken);
        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, periodPayslips.Select(p => p.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);

        var rows = new List<(string Sicil, string AdSoyad, decimal Brut, decimal SgkIsci, decimal GelirVergisi, decimal Damga, decimal Net, decimal MasrafIadesi)>();
        foreach (var payslip in periodPayslips)
        {
            var contact = contacts.GetValueOrDefault(payslip.EmployeeId);
            var adSoyad = contact is null ? payslip.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();

            // "Sicil" - Payroll has no dedicated employee-number field (Employee module doesn't either, verified this
            // tur); EmployeePayrollProfile.ExternalPayrollCode ("harici bordro kodu") is the closest existing concept
            // and is blank for most employees today - left empty rather than falling back to a PII-bearing id.
            var profile = await profiles.FindCurrentAsync(payslip.EmployeeId, periodStart, cancellationToken);
            var sicil = profile?.ExternalPayrollCode ?? string.Empty;

            var trace = await traces.FindLatestByPayslipIdAsync(payslip.Id, cancellationToken);
            var (sgkEmployee, incomeTaxAfterExemption, stampTaxAfterExemption, expenseReimbursement) = ExtractFigures(trace?.StepsJson);

            rows.Add((sicil, adSoyad, payslip.GrossAmount, sgkEmployee, incomeTaxAfterExemption, stampTaxAfterExemption, payslip.NetAmount, expenseReimbursement));
        }

        var table = new ExportTable(
            $"Bordro {period.Year}-{period.Month:D2}",
            [
                new ExportColumn("Sicil"),
                new ExportColumn("Ad Soyad"),
                new ExportColumn("Brüt", ExportColumnType.Number),
                new ExportColumn("SGK İşçi", ExportColumnType.Number),
                new ExportColumn("Gelir Vergisi", ExportColumnType.Number),
                new ExportColumn("Damga Vergisi", ExportColumnType.Number),
                new ExportColumn("Net", ExportColumnType.Number),
                new ExportColumn("Masraf İadesi", ExportColumnType.Number),
            ],
            rows.OrderBy(r => r.AdSoyad, StringComparer.Create(new CultureInfo("tr-TR"), ignoreCase: false))
                .Select(r => (IReadOnlyList<object?>)[r.Sicil, r.AdSoyad, r.Brut, r.SgkIsci, r.GelirVergisi, r.Damga, r.Net, r.MasrafIadesi])
                .ToList());

        return exporter.ExportToExcel(table, cancellationToken);
    }

    /// <summary>Re-derives the after-exemption income/stamp tax and the expense reimbursement NET addition from the
    /// stored trace JSON (see PayrollCalculator: MINWAGE_EXEMPTION_GV/DV steps' Output is the exemption *amount*, not
    /// the after-exemption tax, and NET's expenseReimbursement input is the tax-free masraf iadesi - TB-7).</summary>
    private static (decimal SgkEmployee, decimal IncomeTaxAfterExemption, decimal StampTaxAfterExemption, decimal ExpenseReimbursement) ExtractFigures(string? stepsJson)
    {
        if (string.IsNullOrEmpty(stepsJson))
        {
            return (0m, 0m, 0m, 0m);
        }

        var steps = JsonSerializer.Deserialize<List<TraceStep>>(stepsJson, StepsJsonOptions) ?? [];
        var sgkEmployee = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.SgkEmployee)?.Output ?? 0m;
        var incomeTax = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.IncomeTax)?.Output ?? 0m;
        var minWageExemptionGv = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.MinWageExemptionGv)?.Output ?? 0m;
        var stampTax = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.StampTax)?.Output ?? 0m;
        var minWageExemptionDv = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.MinWageExemptionDv)?.Output ?? 0m;
        var netStep = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.Net);
        var expenseReimbursement = netStep is not null && netStep.Inputs.TryGetValue("expenseReimbursement", out var value)
            ? decimal.Parse(value, CultureInfo.InvariantCulture)
            : 0m;

        return (sgkEmployee, incomeTax - minWageExemptionGv, stampTax - minWageExemptionDv, expenseReimbursement);
    }
}
