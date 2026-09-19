using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.WorkSchedules;

/// <summary>
/// Çalışan kartı "Kariyer &gt; Çalışma Takvimi" alt sekmesi (11.3.6): one dated assignment row. <see cref="EffectiveTo"/>
/// is derived (the next assignment's EffectiveFrom minus one day; null on the chronologically last row, shown as "—"
/// in the UI). <see cref="IsCurrent"/> is the row whose [EffectiveFrom, EffectiveTo] covers today - never the
/// chronologically last row when that one is a future-dated assignment.
/// </summary>
public sealed record EmployeeWorkScheduleAssignmentDto(
    Guid Id,
    Guid WorkScheduleId,
    string WorkScheduleName,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsCurrent,
    WorkScheduleAssignmentSource Source,
    DateTime AssignedAt);

internal static class EmployeeWorkScheduleAssignmentMapper
{
    public static async Task<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>> ToDtosAsync(
        IReadOnlyList<EmployeeWorkScheduleAssignment> assignments, IWorkScheduleRepository schedules, CancellationToken cancellationToken)
    {
        if (assignments.Count == 0)
        {
            return [];
        }

        var names = (await schedules.ListAsync(includeInactive: true, cancellationToken)).ToDictionary(s => s.Id, s => s.Name);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Oldest first so each row can see the next one's EffectiveFrom (its own derived EffectiveTo).
        var ordered = assignments.OrderBy(a => a.EffectiveFrom).ToList();
        var result = new List<EmployeeWorkScheduleAssignmentDto>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i];
            var effectiveTo = i + 1 < ordered.Count ? ordered[i + 1].EffectiveFrom.AddDays(-1) : (DateOnly?)null;
            var isCurrent = current.EffectiveFrom <= today && (effectiveTo is null || effectiveTo >= today);
            result.Add(new EmployeeWorkScheduleAssignmentDto(
                current.Id, current.WorkScheduleId, names.GetValueOrDefault(current.WorkScheduleId, string.Empty),
                current.EffectiveFrom, effectiveTo, isCurrent, current.Source, current.CreatedAt));
        }

        // Newest first for display (Kariyer geçmişi ile aynı sıra - bkz. CareerSection).
        result.Reverse();
        return result;
    }
}

/// <summary>Çalışan kartı "Kariyer &gt; Çalışma Takvimi": HR/yönetici tarafı (`attendance.workschedule.view`).</summary>
[RequiresPermission("attendance.workschedule.view")]
public sealed record ListEmployeeWorkScheduleAssignmentsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>>;

public sealed class ListEmployeeWorkScheduleAssignmentsHandler(
    IEmployeeWorkScheduleAssignmentRepository assignments, IWorkScheduleRepository schedules, IDataScopeService dataScope)
    : IQueryHandler<ListEmployeeWorkScheduleAssignmentsQuery, IReadOnlyList<EmployeeWorkScheduleAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>>> Handle(
        ListEmployeeWorkScheduleAssignmentsQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.WorkScheduleView.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        var list = await assignments.ListForEmployeeAsync(query.EmployeeId, cancellationToken);
        return Result.Success(await EmployeeWorkScheduleAssignmentMapper.ToDtosAsync(list, schedules, cancellationToken));
    }
}

/// <summary>Çalışan kartı "Kariyer &gt; Çalışma Takvimi": self-servis (own card), `attendance.my.workschedule`.</summary>
[RequiresPermission("attendance.my.workschedule")]
public sealed record GetMyWorkScheduleAssignmentsQuery : IQuery<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>>;

public sealed class GetMyWorkScheduleAssignmentsHandler(
    IEmployeeWorkScheduleAssignmentRepository assignments, IWorkScheduleRepository schedules, ICurrentUser currentUser)
    : IQueryHandler<GetMyWorkScheduleAssignmentsQuery, IReadOnlyList<EmployeeWorkScheduleAssignmentDto>>
{
    public async Task<Result<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>>> Handle(
        GetMyWorkScheduleAssignmentsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(AttendanceErrors.EmployeeNotFound);
        }

        var list = await assignments.ListForEmployeeAsync(employeeId, cancellationToken);
        return Result.Success(await EmployeeWorkScheduleAssignmentMapper.ToDtosAsync(list, schedules, cancellationToken));
    }
}
