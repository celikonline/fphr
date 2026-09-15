using FluentValidation;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.WorkSchedules;

public sealed record WorkScheduleDayDto(DayOfWeek DayOfWeek, bool IsWorkingDay, TimeOnly? Start, TimeOnly? End, TimeOnly? BreakStart, TimeOnly? BreakEnd);

public sealed record WorkScheduleDto(
    Guid Id, string Name, bool IsDefault, bool IsActive, bool IncludeSaturdayWhenFullWeek, int WeeklyMinutes, int AssignedEmployeeCount,
    IReadOnlyList<WorkScheduleDayDto> Days);

internal static class WorkScheduleMapper
{
    public static WorkScheduleDto ToDto(WorkSchedule schedule, int assigned) =>
        new(schedule.Id, schedule.Name, schedule.IsDefault, schedule.IsActive, schedule.IncludeSaturdayWhenFullWeek, schedule.WeeklyMinutes, assigned,
            schedule.Days.OrderBy(d => (int)d.DayOfWeek).Select(ToDayDto).ToList());

    public static WorkScheduleDay ToDay(WorkScheduleDayDto dto) =>
        dto.IsWorkingDay
            ? WorkScheduleDay.Working(dto.DayOfWeek, dto.Start ?? default, dto.End ?? default, dto.BreakStart, dto.BreakEnd)
            : WorkScheduleDay.Off(dto.DayOfWeek);

    private static WorkScheduleDayDto ToDayDto(WorkScheduleDay day) =>
        new(day.DayOfWeek, day.IsWorkingDay, day.Start, day.End, day.BreakStart, day.BreakEnd);
}

/// <summary>Work schedules ("Çalışma takvimleri"); seeds the tenant's default schedule on first read.</summary>
[RequiresPermission("attendance.workschedule.view")]
public sealed record ListWorkSchedulesQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<WorkScheduleDto>>;

public sealed class ListWorkSchedulesHandler(
    ITenantContext tenant, IWorkScheduleRepository schedules, WorkScheduleAssignmentService assignmentService, IAttendanceUnitOfWork unitOfWork)
    : IQueryHandler<ListWorkSchedulesQuery, IReadOnlyList<WorkScheduleDto>>
{
    public async Task<Result<IReadOnlyList<WorkScheduleDto>>> Handle(ListWorkSchedulesQuery query, CancellationToken cancellationToken)
    {
        if (await schedules.FindDefaultAsync(cancellationToken) is null)
        {
            await assignmentService.EnsureDefaultScheduleAsync(tenant.TenantId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var list = await schedules.ListAsync(query.IncludeInactive, cancellationToken);
        var result = new List<WorkScheduleDto>(list.Count);
        foreach (var schedule in list)
        {
            result.Add(WorkScheduleMapper.ToDto(schedule, await schedules.CountAssignedEmployeesAsync(schedule.Id, cancellationToken)));
        }

        return result;
    }
}

[RequiresPermission("attendance.workschedule.view")]
public sealed record GetWorkScheduleQuery(Guid WorkScheduleId) : IQuery<WorkScheduleDto>;

public sealed class GetWorkScheduleHandler(IWorkScheduleRepository schedules) : IQueryHandler<GetWorkScheduleQuery, WorkScheduleDto>
{
    public async Task<Result<WorkScheduleDto>> Handle(GetWorkScheduleQuery query, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(query.WorkScheduleId, cancellationToken);
        return schedule.IsFailure
            ? schedule.Error
            : WorkScheduleMapper.ToDto(schedule.Value, await schedules.CountAssignedEmployeesAsync(schedule.Value.Id, cancellationToken));
    }
}

[RequiresPermission("attendance.workschedule.manage")]
public sealed record CreateWorkScheduleCommand(string Name, IReadOnlyList<WorkScheduleDayDto> Days, bool IncludeSaturdayWhenFullWeek) : ICommand<Guid>;

public sealed class CreateWorkScheduleValidator : AbstractValidator<CreateWorkScheduleCommand>
{
    public CreateWorkScheduleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(AttendanceLimits.NameMaxLength);
        RuleFor(x => x.Days).Must(d => d.Count == AttendanceLimits.DaysPerWeek);
    }
}

public sealed class CreateWorkScheduleHandler(ITenantContext tenant, IWorkScheduleRepository schedules) : ICommandHandler<CreateWorkScheduleCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateWorkScheduleCommand command, CancellationToken cancellationToken)
    {
        var created = WorkSchedule.Create(
            tenant.TenantId, command.Name, command.Days.Select(WorkScheduleMapper.ToDay).ToList(), command.IncludeSaturdayWhenFullWeek);
        if (created.IsFailure)
        {
            return Task.FromResult(Result.Failure<Guid>(created.Error));
        }

        schedules.Add(created.Value);
        return Task.FromResult(Result.Success(created.Value.Id));
    }
}

[RequiresPermission("attendance.workschedule.manage")]
public sealed record RenameWorkScheduleCommand(Guid WorkScheduleId, string Name) : ICommand;

public sealed class RenameWorkScheduleValidator : AbstractValidator<RenameWorkScheduleCommand>
{
    public RenameWorkScheduleValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(AttendanceLimits.NameMaxLength);
}

public sealed class RenameWorkScheduleHandler(IWorkScheduleRepository schedules) : ICommandHandler<RenameWorkScheduleCommand>
{
    public async Task<Result> Handle(RenameWorkScheduleCommand command, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(command.WorkScheduleId, cancellationToken);
        return schedule.IsFailure ? schedule.Error : schedule.Value.Rename(command.Name);
    }
}

[RequiresPermission("attendance.workschedule.manage")]
public sealed record UpdateWorkScheduleDaysCommand(Guid WorkScheduleId, IReadOnlyList<WorkScheduleDayDto> Days, bool IncludeSaturdayWhenFullWeek) : ICommand;

public sealed class UpdateWorkScheduleDaysValidator : AbstractValidator<UpdateWorkScheduleDaysCommand>
{
    public UpdateWorkScheduleDaysValidator() => RuleFor(x => x.Days).Must(d => d.Count == AttendanceLimits.DaysPerWeek);
}

public sealed class UpdateWorkScheduleDaysHandler(IWorkScheduleRepository schedules) : ICommandHandler<UpdateWorkScheduleDaysCommand>
{
    public async Task<Result> Handle(UpdateWorkScheduleDaysCommand command, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(command.WorkScheduleId, cancellationToken);
        return schedule.IsFailure
            ? schedule.Error
            : schedule.Value.SetDays(command.Days.Select(WorkScheduleMapper.ToDay).ToList(), command.IncludeSaturdayWhenFullWeek);
    }
}

/// <summary>"Varsayılan yap": exactly one default schedule per tenant afterwards (new employees get it).</summary>
[RequiresPermission("attendance.workschedule.manage")]
public sealed record SetDefaultWorkScheduleCommand(Guid WorkScheduleId) : ICommand;

public sealed class SetDefaultWorkScheduleHandler(IWorkScheduleRepository schedules) : ICommandHandler<SetDefaultWorkScheduleCommand>
{
    public async Task<Result> Handle(SetDefaultWorkScheduleCommand command, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(command.WorkScheduleId, cancellationToken);
        if (schedule.IsFailure)
        {
            return schedule.Error;
        }

        schedule.Value.MakeDefault(await schedules.ListAsync(includeInactive: true, cancellationToken));
        return Result.Success();
    }
}

[RequiresPermission("attendance.workschedule.manage")]
public sealed record SetWorkScheduleActiveCommand(Guid WorkScheduleId, bool IsActive) : ICommand;

public sealed class SetWorkScheduleActiveHandler(IWorkScheduleRepository schedules) : ICommandHandler<SetWorkScheduleActiveCommand>
{
    public async Task<Result> Handle(SetWorkScheduleActiveCommand command, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(command.WorkScheduleId, cancellationToken);
        if (schedule.IsFailure)
        {
            return schedule.Error;
        }

        schedule.Value.SetActive(command.IsActive);
        return Result.Success();
    }
}

/// <summary>Deletes a work schedule; not the default one and not while employees are assigned to it.</summary>
[RequiresPermission("attendance.workschedule.manage")]
public sealed record DeleteWorkScheduleCommand(Guid WorkScheduleId) : ICommand;

public sealed class DeleteWorkScheduleHandler(IWorkScheduleRepository schedules) : ICommandHandler<DeleteWorkScheduleCommand>
{
    public async Task<Result> Handle(DeleteWorkScheduleCommand command, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(command.WorkScheduleId, cancellationToken);
        if (schedule.IsFailure)
        {
            return schedule.Error;
        }

        var deletable = schedule.Value.EnsureDeletable(await schedules.CountAssignedEmployeesAsync(schedule.Value.Id, cancellationToken));
        if (deletable.IsFailure)
        {
            return deletable;
        }

        schedules.Remove(schedule.Value);
        return Result.Success();
    }
}

/// <summary>
/// "Çalışanlar &gt; Toplu işlemler &gt; Çalışma takvimi ekle" (also the single-employee assignment on the employee
/// card): each employee gets a new dated assignment row effective from <paramref name="EffectiveFrom"/>; past
/// AttendanceDay history is never recomputed (11.3.6).
/// </summary>
[RequiresPermission("attendance.workschedule.assign")]
public sealed record AssignWorkScheduleCommand(Guid WorkScheduleId, IReadOnlyList<Guid> EmployeeIds, DateOnly EffectiveFrom) : ICommand;

public sealed class AssignWorkScheduleValidator : AbstractValidator<AssignWorkScheduleCommand>
{
    public const int MaxEmployees = 1000;

    public AssignWorkScheduleValidator()
    {
        RuleFor(x => x.EmployeeIds).NotEmpty();
        RuleFor(x => x.EmployeeIds.Count).LessThanOrEqualTo(MaxEmployees).When(x => x.EmployeeIds is not null);
    }
}

public sealed class AssignWorkScheduleHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IWorkScheduleRepository schedules,
    IEmployeeLookup employeeLookup,
    IDataScopeService dataScope,
    WorkScheduleAssignmentService assignmentService)
    : ICommandHandler<AssignWorkScheduleCommand>
{
    public async Task<Result> Handle(AssignWorkScheduleCommand command, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(command.WorkScheduleId, cancellationToken);
        if (schedule.IsFailure)
        {
            return schedule.Error;
        }

        foreach (var employeeId in command.EmployeeIds.Distinct())
        {
            if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.WorkScheduleAssign.Key, employeeId, cancellationToken))
            {
                return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
            }

            if (!await employeeLookup.ExistsActiveAsync(tenant.TenantId, employeeId, cancellationToken))
            {
                return Error.NotFound(AttendanceErrors.EmployeeNotFound);
            }

            await assignmentService.AssignAsync(
                tenant.TenantId, employeeId, schedule.Value.Id, command.EffectiveFrom, WorkScheduleAssignmentSource.Bulk, currentUser.UserId, cancellationToken);
        }

        return Result.Success();
    }
}
