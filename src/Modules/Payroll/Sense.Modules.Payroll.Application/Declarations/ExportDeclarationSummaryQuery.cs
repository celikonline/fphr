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

/// <summary>
/// The MUHSGK / SGK premium data set of a period (see <see cref="DeclarationSummaryBuilder"/>): per-employee premiums and taxes read from each
/// payslip's calculation trace. Same gate as the APHB draft (payroll.declarations.export). A payslip with no trace still lists, with zeros in the
/// step-based columns, so the file never silently drops someone.
/// </summary>
[RequiresPermission("payroll.declarations.export")]
public sealed record ExportDeclarationSummaryQuery(Guid PeriodId) : IQuery<ExportFileResult>;

public sealed class ExportDeclarationSummaryHandler(
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IPayslipCalculationTraceRepository traces,
    IEmployeeDirectory employeeDirectory,
    ITenantContext tenant) : IQueryHandler<ExportDeclarationSummaryQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(ExportDeclarationSummaryQuery query, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(query.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var periodPayslips = await payslips.ListByPeriodAsync(period.Id, cancellationToken);
        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, periodPayslips.Select(p => p.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);

        var lines = new List<DeclarationLine>();
        foreach (var payslip in periodPayslips)
        {
            var contact = contacts.GetValueOrDefault(payslip.EmployeeId);
            var name = contact is null ? payslip.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
            var trace = await traces.FindLatestByPayslipIdAsync(payslip.Id, cancellationToken);
            lines.Add(FromSteps(name, payslip.GrossAmount, payslip.NetAmount, PayslipTraceSteps.Parse(trace?.StepsJson)));
        }

        var ordered = lines.OrderBy(l => l.EmployeeName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var csv = DeclarationSummaryBuilder.Build(ordered);
        return new ExportFileResult($"beyanname-ozeti_{period.Year}-{period.Month:D2}.csv", "text/csv; charset=utf-8", DeclarationSummaryBuilder.Encode(csv));
    }

    /// <summary>
    /// Income and stamp tax payable are the tax step minus its minimum-wage exemption step (the exemption step's output is the amount taken off);
    /// the other columns are the matching steps' outputs. A missing step counts as zero.
    /// </summary>
    public static DeclarationLine FromSteps(string name, decimal gross, decimal net, IReadOnlyList<TraceStep> steps)
    {
        decimal Output(string code) => steps.FirstOrDefault(s => s.Code == code)?.Output ?? 0m;

        return new DeclarationLine(
            name, gross,
            Output(PayrollTraceStepCodes.SgkEmployee),
            Output(PayrollTraceStepCodes.UnempEmployee),
            Math.Max(0m, Output(PayrollTraceStepCodes.IncomeTax) - Output(PayrollTraceStepCodes.MinWageExemptionGv)),
            Math.Max(0m, Output(PayrollTraceStepCodes.StampTax) - Output(PayrollTraceStepCodes.MinWageExemptionDv)),
            net,
            Output(PayrollTraceStepCodes.EmployerSgk),
            Output(PayrollTraceStepCodes.EmployerUnemp));
    }
}
