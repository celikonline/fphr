using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>
/// Shared "flag this employee dirty in every Open period covering (or after) a date" logic (11.3.07-bordro.md
/// "Değişti mekanizması"). Used both by cross-module integration event handlers (<c>PayslipDirtyFlagHandlers</c>) and
/// by Payroll's own <c>EmployeePayrollProfileFeatures</c> command handlers (profile create/update flags dirty from
/// <c>EffectiveFrom</c> onward). Merges into an already-active flag instead of creating a second row per (period,
/// employee) - see <see cref="PayslipDirtyFlag"/>.
/// </summary>
public sealed class PayslipDirtyFlagService(IPayrollPeriodRepository periods, IPayslipDirtyFlagRepository dirtyFlags, TimeProvider clock)
{
    public async Task FlagAsync(Guid tenantId, Guid employeeId, DateOnly effectiveFrom, string reason, Guid? sourceEventId, CancellationToken cancellationToken)
    {
        var openPeriods = await periods.ListOpenCoveringOrAfterAsync(effectiveFrom, cancellationToken);
        if (openPeriods.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var period in openPeriods)
        {
            var existing = await dirtyFlags.FindActiveAsync(period.Id, employeeId, cancellationToken);
            if (existing is not null)
            {
                existing.AddReason(reason, sourceEventId, now);
            }
            else
            {
                dirtyFlags.Add(PayslipDirtyFlag.Create(tenantId, period.Id, employeeId, reason, sourceEventId, now));
            }
        }
    }
}
