using System.Text;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>Bir dönemin APHB TASLAK dosyasını indirir (senseik-019 3b). payroll.declarations.export gerektirir -
/// SystemRoleDefinitions'daki "payroll.declarations.*" içeren muhasebe/bordro rolüne otomatik dahildir.</summary>
[RequiresPermission("payroll.declarations.export")]
public sealed record ExportAphbFileQuery(Guid PeriodId) : IQuery<ExportFileResult>;

public sealed class ExportAphbFileHandler(
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IPayslipCalculationTraceRepository traces,
    IEmployeePayrollProfileRepository profiles,
    IEmployeeDirectory employeeDirectory,
    ITenantContext tenant) : IQueryHandler<ExportAphbFileQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(ExportAphbFileQuery query, CancellationToken cancellationToken)
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

        var lines = new List<AphbLine>();
        foreach (var payslip in periodPayslips)
        {
            var contact = contacts.GetValueOrDefault(payslip.EmployeeId);
            var fullName = contact is null ? payslip.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
            var profile = await profiles.FindCurrentAsync(payslip.EmployeeId, periodStart, cancellationToken);
            var trace = await traces.FindLatestByPayslipIdAsync(payslip.Id, cancellationToken);
            var figures = ExtractFigures(trace?.StepsJson);

            lines.Add(new AphbLine(
                NationalId: string.Empty, // TODO senseik-009 (sicil/uyruk kartı) tamamlanınca TCKN portu eklenecek
                FullName: fullName,
                SicilNo: profile?.ExternalPayrollCode ?? string.Empty,
                SgkWorkplaceCode: string.Empty, // TODO senseik-010 (SGK işyeri sicil no) tamamlanınca eklenecek
                PrimOdemeGunSayisi: figures.WorkedDays,
                PrimeEsasKazanc: figures.SgkBase,
                SigortaliPayi: figures.SigortaliPayi,
                IsverenPayi: figures.IsverenPayi,
                IncentiveLawCode: profile?.IncentiveLawCode));
        }

        var content = AphbFileBuilder.BuildContent(period.Year, period.Month, lines);
        return new ExportFileResult($"APHB_{period.Year}-{period.Month:D2}.txt", "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(content));
    }

    private static (int WorkedDays, decimal SgkBase, decimal SigortaliPayi, decimal IsverenPayi) ExtractFigures(string? stepsJson)
    {
        if (string.IsNullOrEmpty(stepsJson))
        {
            return (0, 0m, 0m, 0m);
        }

        var steps = PayslipTraceSteps.Parse(stepsJson);
        var workedDays = (int)(steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.Days)?.Output ?? 0m);
        var sgkBase = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.SgkBase)?.Output ?? 0m;
        var sgkEmployee = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.SgkEmployee)?.Output ?? 0m;
        var unempEmployee = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.UnempEmployee)?.Output ?? 0m;
        var employerSgk = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.EmployerSgk)?.Output ?? 0m;
        var employerDiscount = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.EmployerDiscount)?.Output ?? 0m;
        var employerUnemp = steps.FirstOrDefault(s => s.Code == PayrollTraceStepCodes.EmployerUnemp)?.Output ?? 0m;

        return (workedDays, sgkBase, sgkEmployee + unempEmployee, employerSgk - employerDiscount + employerUnemp);
    }
}
