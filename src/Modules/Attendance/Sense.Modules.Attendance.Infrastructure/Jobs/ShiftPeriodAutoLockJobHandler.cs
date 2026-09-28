using Microsoft.Extensions.Logging;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Messaging;

namespace Sense.Modules.Attendance.Infrastructure.Jobs;

/// <summary>
/// K7: the seeded <see cref="JobType.ShiftPeriodAutoLock"/> recurring job used to have no handler. Sends
/// <see cref="LockDueShiftPeriodsCommand"/> through the dispatcher (UnitOfWorkBehaviour commits) for the job's tenant.
/// </summary>
public sealed partial class ShiftPeriodAutoLockJobHandler(IDispatcher dispatcher, TimeProvider clock, ILogger<ShiftPeriodAutoLockJobHandler> logger)
    : IScheduledJobHandler
{
    public JobType Handles => JobType.ShiftPeriodAutoLock;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        if (context.TenantId is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var result = await dispatcher.Send(new LockDueShiftPeriodsCommand(today), ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            LogFailed(logger, context.TenantId.Value, result.Error.Code);
        }
    }

    [LoggerMessage(EventId = 7411, Level = LogLevel.Warning, Message = "Shift period auto-lock failed for tenant {TenantId}: {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, Guid tenantId, string errorCode);
}
