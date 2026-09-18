using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
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
    IAttendanceDayRepository days, IShiftSettingsRepository shiftSettings, IAttendanceUnitOfWork unitOfWork)
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

        var overnightDates = await ReassignNightShiftPunchesAsync(plans, days, cancellationToken).ConfigureAwait(false);
        var limits = ToShiftLimits(await shiftSettings.FindAsync(cancellationToken).ConfigureAwait(false));

        var imported = 0;
        var anyChange = false;
        foreach (var group in plans.GroupBy(p => (p.EmployeeId, p.Date)))
        {
            var isOvernight = overnightDates.Contains(group.Key);
            var groupList = group.ToList();
            var ins = groupList.Where(p => p.Direction == DeviceLogDirection.In).ToList();
            var outs = groupList.Where(p => p.Direction == DeviceLogDirection.Out).ToList();
            var batchCheckIn = ins.Count > 0 ? TimeOnly.FromDateTime(ins.Min(p => p.Timestamp)) : (TimeOnly?)null;
            var batchCheckOut = outs.Count > 0 ? TimeOnly.FromDateTime(outs.Max(p => p.Timestamp)) : (TimeOnly?)null;

            var existing = await days.FindByEmployeeAndDateAsync(group.Key.EmployeeId, group.Key.Date, cancellationToken).ConfigureAwait(false);

            // G-6-22: PDKS exports commonly arrive in separate partial batches (e.g. only the "out" punch, the "in"
            // having already landed in an earlier import). A direction missing from *this* batch must not erase the
            // day's already-recorded field - fall back to the existing value instead of writing null over it.
            var checkIn = batchCheckIn ?? existing?.CheckIn;
            var checkOut = batchCheckOut ?? existing?.CheckOut;

            if (!isOvernight && checkIn is not null && checkOut is not null && checkOut <= checkIn)
            {
                foreach (var plan in group)
                {
                    errors.Add(new DeviceLogImportErrorDto(plan.Line, DeviceLogErrorCodes.CheckOutBeforeCheckIn, null));
                }

                continue;
            }

            // G-02 mola tespiti (kart senseik-014 §1): ilk "in" ile son "out" arasındaki ara Out→In çiftleri gerçek
            // moladır. Bu batch tek yönlü bir kısmi import ise (yalnız "out") 0 çıkar - önceki gözlemi ezmesin diye
            // daha önce kaydedilmiş değere düşülür (aynı G-6-22 kalıbı).
            var computedBreak = ComputeActualBreakMinutes(groupList);
            var actualBreakMinutes = computedBreak > 0 ? computedBreak : existing?.ActualBreakMinutes ?? 0;

            var (start, end, minutes, breakMinutes) = await PlannedDayResolver.ResolveAsync(schedules, group.Key.EmployeeId, group.Key.Date, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                existing.UpdateRecord(checkIn, checkOut, start, end, minutes, breakMinutes, AttendanceDaySource.Device, isOvernight, actualBreakMinutes, limits);
            }
            else
            {
                var day = AttendanceDay.Record(
                    tenant.TenantId, group.Key.EmployeeId, group.Key.Date, checkIn, checkOut, start, end, minutes, breakMinutes, AttendanceDaySource.Device,
                    isOvernight, actualBreakMinutes, limits);
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

    /// <summary>
    /// Night shift day assignment (G-6-14, 4857 md.69): a morning "out" punch (before 12:00) that has no earlier
    /// "in" punch the same day is attributed to the previous calendar day when that previous day has an unmatched
    /// afternoon/evening "in" (12:00 or later, no later "out" already pairs with it that day) - either another row
    /// in this same file, or the previous day's existing <see cref="AttendanceDay"/> record (check-in set, check-out
    /// still empty). Every other case is left on its own day (today's behavior); no query against the planned shift
    /// is made in this first version. Mutates the plans in place (moves the "out" to the previous date) and returns
    /// the (employee, date) pairs that must be treated as overnight when the day is recorded.
    /// </summary>
    private static async Task<HashSet<(Guid EmployeeId, DateOnly Date)>> ReassignNightShiftPunchesAsync(
        List<Plan> plans, IAttendanceDayRepository days, CancellationToken cancellationToken)
    {
        var noon = new TimeOnly(12, 0);
        var overnightDates = new HashSet<(Guid EmployeeId, DateOnly Date)>();

        for (var i = 0; i < plans.Count; i++)
        {
            var candidate = plans[i];
            if (candidate.Direction != DeviceLogDirection.Out || TimeOnly.FromDateTime(candidate.Timestamp) >= noon)
            {
                continue;
            }

            var sameDayEarlierIn = plans.Any(p =>
                p.EmployeeId == candidate.EmployeeId && p.Date == candidate.Date &&
                p.Direction == DeviceLogDirection.In && p.Timestamp < candidate.Timestamp);
            if (sameDayEarlierIn)
            {
                continue;
            }

            var previousDate = candidate.Date.AddDays(-1);
            var unmatchedPreviousIn = plans.Any(inRow =>
                inRow.EmployeeId == candidate.EmployeeId && inRow.Date == previousDate &&
                inRow.Direction == DeviceLogDirection.In && TimeOnly.FromDateTime(inRow.Timestamp) >= noon &&
                !plans.Any(laterOut =>
                    laterOut.EmployeeId == inRow.EmployeeId && laterOut.Date == inRow.Date &&
                    laterOut.Direction == DeviceLogDirection.Out && laterOut.Timestamp > inRow.Timestamp));

            var matchesExistingRecord = false;
            if (!unmatchedPreviousIn)
            {
                var existingPrevious = await days.FindByEmployeeAndDateAsync(candidate.EmployeeId, previousDate, cancellationToken).ConfigureAwait(false);
                matchesExistingRecord = existingPrevious is { CheckOut: null, CheckIn: not null } && existingPrevious.CheckIn.Value >= noon;
            }

            if (!unmatchedPreviousIn && !matchesExistingRecord)
            {
                continue;
            }

            plans[i] = candidate with { Date = previousDate };
            overnightDates.Add((candidate.EmployeeId, previousDate));
        }

        return overnightDates;
    }

    /// <summary>
    /// G-02 mola tespiti (kart senseik-014 §1): sorts the group chronologically, then sums every Out→In gap strictly
    /// between the first "in" and the last "out" - the same "ilk in / son out" window the check-in/check-out derivation
    /// above already uses. Punches outside that window (duplicates, stray badges) don't contribute.
    /// </summary>
    private static int ComputeActualBreakMinutes(List<Plan> group)
    {
        var ordered = group.OrderBy(p => p.Timestamp).ToList();
        var firstIn = ordered.FirstOrDefault(p => p.Direction == DeviceLogDirection.In);
        var lastOut = ordered.LastOrDefault(p => p.Direction == DeviceLogDirection.Out);
        if (firstIn is null || lastOut is null || lastOut.Timestamp <= firstIn.Timestamp)
        {
            return 0;
        }

        var inner = ordered.Where(p => p.Timestamp > firstIn.Timestamp && p.Timestamp < lastOut.Timestamp).ToList();
        var total = 0;
        for (var i = 0; i < inner.Count - 1; i++)
        {
            if (inner[i].Direction == DeviceLogDirection.Out && inner[i + 1].Direction == DeviceLogDirection.In)
            {
                total += (int)(inner[i + 1].Timestamp - inner[i].Timestamp).TotalMinutes;
            }
        }

        return total;
    }

    private static ShiftLimits ToShiftLimits(ShiftSettings? settings) =>
        settings is null
            ? ShiftLimits.None
            : new ShiftLimits(settings.MaxDailyMinutes, settings.NightWorkEnabled, settings.NightWorkStart, settings.NightWorkEnd, settings.NightWorkMaxMinutes);
}
