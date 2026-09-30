using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Modules.Workflow.Contracts;

namespace Sense.Modules.Payroll.Infrastructure.Lookups;

/// <summary>
/// Workflow.Contracts.IApprovalRequestSummaryProvider for RequestType "PayslipObjection" (E2): the payroll period label
/// ("2026-08") and the employee's objection reason, so the approver decides without opening the payslip. Amounts are not
/// projected. AsNoTracking - a display read, not a unit of work.
/// </summary>
public sealed class PayslipObjectionApprovalSummaryProvider(PayrollDbContext context) : IApprovalRequestSummaryProvider
{
    public string RequestType => ApprovalRequestTypes.PayslipObjection;

    public async Task<ApprovalRequestSummary?> GetSummaryAsync(Guid tenantId, Guid subjectId, CancellationToken cancellationToken = default)
    {
        var row = await (
                from payslip in context.Payslips.AsNoTracking()
                join period in context.Periods.AsNoTracking() on payslip.PayrollPeriodId equals period.Id
                where payslip.TenantId == tenantId && payslip.Id == subjectId
                select new { period.Year, period.Month, payslip.ObjectionReason, payslip.ObjectedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var label = string.Create(CultureInfo.InvariantCulture, $"{row.Year:D4}-{row.Month:D2}");
        var requestDate = row.ObjectedAt is { } objectedAt ? DateOnly.FromDateTime(objectedAt.UtcDateTime) : new DateOnly(row.Year, row.Month, 1);
        return new ApprovalRequestSummary(requestDate, PayslipObjection: new PayslipObjectionApprovalSummary(label, row.ObjectionReason));
    }
}
