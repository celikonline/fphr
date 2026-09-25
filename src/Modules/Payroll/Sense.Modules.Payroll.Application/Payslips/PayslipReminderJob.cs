using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Payslips;

/// <summary>
/// E2 "imzalamayanlara hatırlatma": collects the tenant's published payslips that still await acknowledgement
/// (<see cref="Domain.Payslips.Payslip.AwaitsAcknowledgement"/> - no pending/accepted objection) in paid or locked
/// periods and enqueues one <see cref="PayslipAcknowledgementReminderDue"/> for Notifications. The caller saves.
/// </summary>
public sealed class PayslipAcknowledgementReminderService(IPayslipRepository payslips, IPayrollPeriodRepository periods, IIntegrationEventOutbox outbox)
{
    /// <summary>
    /// Returns how many payslips were included (0 = nothing enqueued). <paramref name="publishedSince"/> limits the pass
    /// to recent publications (daily job); null = no limit (manual trigger for one period).
    /// </summary>
    public async Task<int> EnqueueRemindersAsync(Guid tenantId, Guid? periodId, DateTimeOffset? publishedSince, CancellationToken cancellationToken)
    {
        var candidates = await payslips.ListPublishedUnacknowledgedAsync(periodId, publishedSince, cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var periodsById = (await periods.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        var due = candidates
            .Where(p => p.AwaitsAcknowledgement && (publishedSince is null || p.PublishedAt >= publishedSince))
            .Select(p => (Payslip: p, Period: periodsById.GetValueOrDefault(p.PayrollPeriodId)))
            .Where(x => x.Period is not null && MyPayslips.IsPublished(x.Payslip, x.Period))
            .Select(x => new UnacknowledgedPayslip(x.Payslip.Id, x.Payslip.EmployeeId, x.Period!.Id, x.Period.Year, x.Period.Month))
            .ToList();
        if (due.Count == 0)
        {
            return 0;
        }

        outbox.Enqueue(new PayslipAcknowledgementReminderDue(tenantId, due));
        return due.Count;
    }
}

/// <summary>
/// ADR-0004 handler for <see cref="JobType.PayslipAcknowledgementReminder"/> (Hangfire, Sense.Api): per-tenant daily
/// pass over <see cref="PayslipAcknowledgementReminderService"/>, limited to payslips published in the last
/// <see cref="PayrollLimits.AcknowledgementReminderWindowDays"/> days (older periods are not chased forever).
/// </summary>
public sealed class PayslipAcknowledgementReminderJobHandler(
    PayslipAcknowledgementReminderService reminders, IPayrollUnitOfWork unitOfWork, TimeProvider clock)
    : IScheduledJobHandler
{
    public JobType Handles => JobType.PayslipAcknowledgementReminder;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        if (context.TenantId is not { } tenantId)
        {
            return;
        }

        var publishedSince = clock.GetUtcNow().AddDays(-PayrollLimits.AcknowledgementReminderWindowDays);
        if (await reminders.EnqueueRemindersAsync(tenantId, periodId: null, publishedSince, ct) > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

/// <summary>Manual "Hatırlat" for one paid/locked period (E2 "elle tetik"); returns how many employees are reminded.</summary>
[RequiresPermission("payroll.period.approve")]
public sealed record SendPayslipAcknowledgementRemindersCommand(Guid PeriodId) : ICommand<int>;

public sealed class SendPayslipAcknowledgementRemindersHandler(
    ITenantContext tenant, IPayrollPeriodRepository periods, PayslipAcknowledgementReminderService reminders)
    : ICommandHandler<SendPayslipAcknowledgementRemindersCommand, int>
{
    public async Task<Result<int>> Handle(SendPayslipAcknowledgementRemindersCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        if (period.Status is not (PayrollPeriodStatus.Paid or PayrollPeriodStatus.Locked))
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotPaid);
        }

        return await reminders.EnqueueRemindersAsync(tenant.TenantId, period.Id, publishedSince: null, cancellationToken);
    }
}
