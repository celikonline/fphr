using Sense.Modules.Attendance.Contracts;
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
public sealed record EnqueueRecalculationCommand(DateOnly From, DateOnly To, IReadOnlyCollection<Guid>? EmployeeIds) : ICommand<Guid>;

public sealed class EnqueueRecalculationHandler(IAttendanceDayRecalculator recalculator, ITenantContext tenant)
    : ICommandHandler<EnqueueRecalculationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(EnqueueRecalculationCommand command, CancellationToken cancellationToken)
    {
        if (command.To < command.From)
        {
            return Error.Validation(AttendanceErrors.RecalculationRangeInvalid);
        }

        if (command.To.DayNumber - command.From.DayNumber >= RecalculationLimits.MaxRangeDays)
        {
            return Error.Validation(AttendanceErrors.RecalculationRangeTooLong);
        }

        return await recalculator.EnqueueAsync(tenant.TenantId, command.From, command.To, command.EmployeeIds, cancellationToken);
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
