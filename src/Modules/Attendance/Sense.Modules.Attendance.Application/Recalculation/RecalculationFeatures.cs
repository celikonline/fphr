using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Recalculation;

public static class RecalculationLimits
{
    public const int MaxRangeDays = 366;
}

[RequiresPermission("attendance.recalculate.run")]
public sealed record EnqueueRecalculationCommand(DateOnly From, DateOnly To, IReadOnlyCollection<Guid>? EmployeeIds) : ICommand<EnqueueRecalculationResult>;

/// <param name="SkippedOutOfScope">Requested employees dropped because they are outside the caller's data scope.</param>
public sealed record EnqueueRecalculationResult(Guid JobId, int SkippedOutOfScope);

public sealed class EnqueueRecalculationHandler(
    IAttendanceDayRecalculator recalculator, ITenantContext tenant, IDataScopeService dataScope, IEmployeeDirectory employees)
    : ICommandHandler<EnqueueRecalculationCommand, EnqueueRecalculationResult>
{
    public async Task<Result<EnqueueRecalculationResult>> Handle(EnqueueRecalculationCommand command, CancellationToken cancellationToken)
    {
        if (command.To < command.From)
        {
            return Error.Validation(AttendanceErrors.RecalculationRangeInvalid);
        }

        if (command.To.DayNumber - command.From.DayNumber >= RecalculationLimits.MaxRangeDays)
        {
            return Error.Validation(AttendanceErrors.RecalculationRangeTooLong);
        }

        // The background job has no caller, so the data scope is applied here, at enqueue time.
        var visible = await dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.RecalculateRun.Key, cancellationToken);
        var skipped = 0;
        IReadOnlyCollection<Guid>? ids;
        if (command.EmployeeIds is { Count: > 0 })
        {
            var requested = command.EmployeeIds.Distinct().ToList();
            ids = visible is null ? requested : requested.Where(visible.Contains).ToList();
            skipped = requested.Count - ids.Count;
        }
        else if (visible is null)
        {
            ids = null;
        }
        else
        {
            var active = await employees.ListActiveEmployeeIdsAsync(tenant.TenantId, cancellationToken);
            ids = active.Where(visible.Contains).ToList();
        }

        if (ids is { Count: 0 })
        {
            return new EnqueueRecalculationResult(Guid.Empty, skipped);
        }

        var jobId = await recalculator.EnqueueAsync(tenant.TenantId, command.From, command.To, ids, cancellationToken);
        return new EnqueueRecalculationResult(jobId, skipped);
    }
}

[RequiresPermission("attendance.recalculate.run")]
public sealed record GetRecalculationStatusQuery(Guid JobId) : IQuery<RecalculationStatusDto>;

public sealed class GetRecalculationStatusHandler(IAttendanceDayRecalculator recalculator, ITenantContext tenant)
    : IQueryHandler<GetRecalculationStatusQuery, RecalculationStatusDto>
{
    public async Task<Result<RecalculationStatusDto>> Handle(GetRecalculationStatusQuery query, CancellationToken cancellationToken)
    {
        var status = await recalculator.GetStatusAsync(tenant.TenantId, query.JobId, cancellationToken);
        return status is null ? Error.NotFound(AttendanceErrors.RecalculationJobNotFound) : status;
    }
}
