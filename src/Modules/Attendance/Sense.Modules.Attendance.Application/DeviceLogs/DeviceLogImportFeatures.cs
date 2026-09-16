using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Attendance.Application.DeviceLogs;

public sealed record DeviceLogImportErrorDto(int Line, string Code, string? Value);

public sealed record DeviceLogImportResultDto(int Imported, int Skipped, IReadOnlyList<DeviceLogImportErrorDto> Errors);

/// <summary>
/// S-11 PDKS cihaz/turnike CSV içe alma (ACİL KARAR 16:52): her satır tek bir "in"/"out" damgasıdır; aynı çalışan+gün
/// için toplanan damgalardan tek bir <see cref="AttendanceDay"/> (en erken "in" = check-in, en geç "out" = check-out)
/// türetilir - mevcut manuel/Excel kaydıyla aynı upsert kalıbı, kaynağı <see cref="AttendanceDaySource.Device"/>. Bozuk satırlar
/// dosyanın tamamını reddetmez, tek tek atlanır (satır numarası + hata koduyla raporlanır).
/// </summary>
[RequiresPermission(AttendancePermissions.TimesheetImportKey)]
public sealed record ImportDeviceLogsCommand(string FileContent) : ICommand<DeviceLogImportResultDto>;

public sealed class ImportDeviceLogsHandler(
    ITenantContext tenant, IEmployeeLookup employeeLookup, IWorkScheduleReadService schedules,
    IAttendanceDayRepository days, IAttendanceUnitOfWork unitOfWork)
    : ICommandHandler<ImportDeviceLogsCommand, DeviceLogImportResultDto>
{
    private sealed record Plan(int Line, Guid EmployeeId, DateOnly Date, DateTime Timestamp, DeviceLogDirection Direction);

    public async Task<Result<DeviceLogImportResultDto>> Handle(ImportDeviceLogsCommand command, CancellationToken cancellationToken)
    {
        var parsed = DeviceLogCsvParser.Parse(command.FileContent);
        var errors = new List<DeviceLogImportErrorDto>();
        foreach (var parseError in parsed.Errors)
        {
            errors.Add(new DeviceLogImportErrorDto(parseError.Line, parseError.Code, parseError.Value));
        }

        var employeeCache = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var seenTimestamps = new HashSet<(Guid EmployeeId, DateTime Timestamp)>();
        var plans = new List<Plan>();

        foreach (var row in parsed.Rows)
        {
            if (!employeeCache.TryGetValue(row.EmployeeCode, out var employeeId))
            {
                employeeId = await employeeLookup.FindActiveByNationalIdAsync(tenant.TenantId, row.EmployeeCode, cancellationToken).ConfigureAwait(false);
                employeeCache[row.EmployeeCode] = employeeId;
            }

            if (employeeId is null)
            {
                errors.Add(new DeviceLogImportErrorDto(row.Line, DeviceLogErrorCodes.UnknownEmployee, row.EmployeeCode));
                continue;
            }

            if (!seenTimestamps.Add((employeeId.Value, row.Timestamp)))
            {
                errors.Add(new DeviceLogImportErrorDto(row.Line, DeviceLogErrorCodes.DuplicateTimestamp, row.EmployeeCode));
                continue;
            }

            plans.Add(new Plan(row.Line, employeeId.Value, DateOnly.FromDateTime(row.Timestamp), row.Timestamp, row.Direction));
        }

        var imported = 0;
        var anyChange = false;
        foreach (var group in plans.GroupBy(p => (p.EmployeeId, p.Date)))
        {
            var ins = group.Where(p => p.Direction == DeviceLogDirection.In).ToList();
            var outs = group.Where(p => p.Direction == DeviceLogDirection.Out).ToList();
            var checkIn = ins.Count > 0 ? TimeOnly.FromDateTime(ins.Min(p => p.Timestamp)) : (TimeOnly?)null;
            var checkOut = outs.Count > 0 ? TimeOnly.FromDateTime(outs.Max(p => p.Timestamp)) : (TimeOnly?)null;

            if (checkIn is not null && checkOut is not null && checkOut <= checkIn)
            {
                foreach (var plan in group)
                {
                    errors.Add(new DeviceLogImportErrorDto(plan.Line, DeviceLogErrorCodes.CheckOutBeforeCheckIn, null));
                }

                continue;
            }

            var (start, end, minutes, breakMinutes) = await PlannedDayResolver.ResolveAsync(schedules, group.Key.EmployeeId, group.Key.Date, cancellationToken).ConfigureAwait(false);
            var existing = await days.FindByEmployeeAndDateAsync(group.Key.EmployeeId, group.Key.Date, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                existing.UpdateRecord(checkIn, checkOut, start, end, minutes, breakMinutes, AttendanceDaySource.Device);
            }
            else
            {
                var day = AttendanceDay.Record(tenant.TenantId, group.Key.EmployeeId, group.Key.Date, checkIn, checkOut, start, end, minutes, breakMinutes, AttendanceDaySource.Device);
                days.Add(day);
            }

            imported += group.Count();
            anyChange = true;
        }

        if (anyChange)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new DeviceLogImportResultDto(imported, errors.Count, errors.OrderBy(e => e.Line).ToList());
    }
}
