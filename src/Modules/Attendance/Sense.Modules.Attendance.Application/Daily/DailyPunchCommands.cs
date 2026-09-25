using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.Daily;

public sealed record DailyPunchCreatedDto(Guid Id);

/// <summary>Elle okutma ekle (spec 5.4): gerekçe zorunlu, veri kapsamı, K6 kilidi; kayıt ve gün yeniden hesabı IPunchRecorder'da.</summary>
[RequiresPermission(AttendancePermissions.PunchManageKey)]
public sealed record AddDailyPunchCommand(Guid EmployeeId, DateTime OccurredAtUtc, PunchDirectionDto Direction, string Reason) : ICommand<DailyPunchCreatedDto>;

public sealed class AddDailyPunchHandler(
    ITenantContext tenant, ICurrentUser currentUser, IDataScopeService dataScope, IPunchRecorder recorder,
    IMonthlyTimesheetApprovalRepository approvals, ITenantTimeZoneLookup timeZones) : ICommandHandler<AddDailyPunchCommand, DailyPunchCreatedDto>
{
    public async Task<Result<DailyPunchCreatedDto>> Handle(AddDailyPunchCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation(AttendanceErrors.PunchReasonRequired);
        }

        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.PunchManageKey, command.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        var localDate = await LocalDateAsync(command.OccurredAtUtc, cancellationToken);
        if (await PunchPeriodGuard.IsLockedAsync(approvals, command.EmployeeId, localDate, cancellationToken))
        {
            return Error.Conflict(AttendanceErrors.PeriodLocked);
        }

        var input = new PunchInput(
            tenant.TenantId, command.EmployeeId, command.OccurredAtUtc, command.Direction, PunchSourceDto.Manual, null, null, null, null, null,
            IsManual: true, currentUser.UserId, command.Reason.Trim());
        var recorded = await recorder.RecordAsync(input, cancellationToken);
        return recorded.IsFailure ? recorded.Error : new DailyPunchCreatedDto(recorded.Value);
    }

    private async Task<DateOnly> LocalDateAsync(DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var zoneId = await timeZones.FindTimeZoneIdAsync(tenant.TenantId, cancellationToken);
        var zone = zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        var utc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }
}

/// <summary>Okutma sil (yumuşak, gerekçeli): veri kapsamı ve K6 kilidi burada, denetim izi Punch (IAuditLogged) + recorder'da.</summary>
[RequiresPermission(AttendancePermissions.PunchManageKey)]
public sealed record DeleteDailyPunchCommand(Guid PunchId, string Reason) : ICommand;

public sealed class DeleteDailyPunchHandler(
    ITenantContext tenant, ICurrentUser currentUser, IDataScopeService dataScope, IPunchRecorder recorder, IPunchRepository punches,
    IMonthlyTimesheetApprovalRepository approvals) : ICommandHandler<DeleteDailyPunchCommand>
{
    public async Task<Result> Handle(DeleteDailyPunchCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Error.Validation(AttendanceErrors.PunchReasonRequired);
        }

        var punch = await punches.FindAsync(tenant.TenantId, command.PunchId, cancellationToken);
        if (punch is null)
        {
            return Error.NotFound(AttendanceErrors.PunchNotFound);
        }

        if (!await dataScope.CanAccessEmployeeAsync(AttendancePermissions.PunchManageKey, punch.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(AttendanceErrors.EmployeeNotInScope);
        }

        if (await PunchPeriodGuard.IsLockedAsync(approvals, punch.EmployeeId, punch.WorkDate, cancellationToken))
        {
            return Error.Conflict(AttendanceErrors.PeriodLocked);
        }

        if (currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(AttendanceErrors.EmployeeNotInScope);
        }

        return await recorder.SoftDeleteAsync(tenant.TenantId, command.PunchId, userId, command.Reason.Trim(), cancellationToken);
    }
}

/// <summary>K6: a month with an approved MonthlyTimesheetApproval rejects punch add/delete.</summary>
internal static class PunchPeriodGuard
{
    public static async Task<bool> IsLockedAsync(IMonthlyTimesheetApprovalRepository approvals, Guid employeeId, DateOnly date, CancellationToken cancellationToken) =>
        await approvals.FindAsync(employeeId, MonthlyTimesheetApproval.NormalizeToMonthStart(date), cancellationToken) is not null;
}
