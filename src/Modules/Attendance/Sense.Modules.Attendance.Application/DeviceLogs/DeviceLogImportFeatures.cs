using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
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
/// S-11 PDKS cihaz/turnike CSV içe alma (ACİL KARAR 16:52): her satır tek bir "in"/"out" damgasıdır ve (Z0.1) bir ham okutma
/// (<see cref="Domain.Punches.Punch"/>, kaynağı Device) olarak saklanır; çalışan+günün <see cref="AttendanceDay"/>'i o günün tüm
/// okutmalarından türetilir (ilk "in" = check-in, son "out" = check-out, ara Out→In = gerçek mola; bozuk yön sırası Incalculable).
/// Aynı damga önceki bir aktarımda zaten varsa yeniden yazılmaz (idempotent). Bozuk satırlar dosyanın tamamını reddetmez, tek tek
/// atlanır (satır numarası + hata koduyla raporlanır).
/// </summary>
[RequiresPermission(AttendancePermissions.TimesheetImportKey)]
public sealed record ImportDeviceLogsCommand(string FileContent) : ICommand<DeviceLogImportResultDto>;

public sealed class ImportDeviceLogsHandler(
    ITenantContext tenant, IEmployeeLookup employeeLookup, IPlannedDayResolver planner,
    IAttendanceDayRepository days, IShiftSettingsRepository shiftSettings, IAttendanceUnitOfWork unitOfWork,
    IPunchRepository punches, ITenantLocalClock clock, IMonthlyTimesheetApprovalRepository approvals)
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
        var limits = AttendanceDayUpsert.ToShiftLimits(await shiftSettings.FindAsync(cancellationToken).ConfigureAwait(false));
        var now = await clock.NowAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        var zone = await clock.GetTimeZoneAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        var writer = new PunchDayWriter(tenant.TenantId, days, punches, planner, approvals, zone, now.Utc);

        var imported = 0;
        var anyChange = false;
        foreach (var group in plans.GroupBy(p => (p.EmployeeId, p.Date)))
        {
            var groupList = group.ToList();

            // K6: onaylı aylık puantaj ayına ait satırlar işi bozmadan atlanır (satır numarası + neden raporlanır).
            if (await writer.IsLockedAsync(group.Key.EmployeeId, group.Key.Date, cancellationToken).ConfigureAwait(false))
            {
                errors.AddRange(groupList.Select(plan => new DeviceLogImportErrorDto(plan.Line, AttendanceErrors.PeriodLocked, null)));
                continue;
            }

            if (await HasReversedPairAsync(groupList, overnightDates.Contains(group.Key), cancellationToken).ConfigureAwait(false))
            {
                errors.AddRange(groupList.Select(plan => new DeviceLogImportErrorDto(plan.Line, DeviceLogErrorCodes.CheckOutBeforeCheckIn, null)));
                continue;
            }

            // G-6-22: PDKS exports commonly arrive in separate partial batches (e.g. only the "out" punch, the "in" having already
            // landed in an earlier import). Z0.1 ile bu doğal: gün, önceki aktarımların okutmalarıyla birlikte yeniden türetilir (okutması
            // olmayan eski bir günün giriş/çıkışı da önce okutmaya çevrilir) - bu batch'te olmayan yön önceki değeri ezmez.
            var dayPunches = await writer.LoadDayAsync(group.Key.EmployeeId, group.Key.Date, seedLegacy: true, cancellationToken).ConfigureAwait(false);
            var failed = AddPunches(writer, groupList, dayPunches, zone, errors);
            if (failed)
            {
                continue;
            }

            var rebuilt = await writer.RebuildAsync(
                group.Key.EmployeeId, group.Key.Date, AttendanceDaySource.Device, limits, forceIncalculable: false, cancellationToken).ConfigureAwait(false);
            if (rebuilt.IsFailure)
            {
                errors.AddRange(groupList.Select(plan => new DeviceLogImportErrorDto(plan.Line, DeviceLogErrorCodes.CheckOutBeforeCheckIn, null)));
                continue;
            }

            imported += groupList.Count;
            anyChange = true;
        }

        if (anyChange)
        {
            // Eşzamanlı yazma çakışması (aynı gün / aynı okutma / günün xmin'i) yazıcıda yeniden türetilip denenir.
            var saved = await writer.SaveAsync(unitOfWork, cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                return saved.Error;
            }
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
    /// Bugünkü doğrulama korunur: bu batch'in en erken "in"i / en geç "out"u (yoksa günün kayıtlı giriş/çıkışı) gece vardiyası
    /// değilken ters sıradaysa (çıkış ≤ giriş) grubun hiçbir satırı yazılmaz - bozuk cihaz verisi okutma olarak da saklanmaz.
    /// </summary>
    private async Task<bool> HasReversedPairAsync(List<Plan> group, bool isOvernight, CancellationToken cancellationToken)
    {
        if (isOvernight)
        {
            return false;
        }

        var ins = group.Where(p => p.Direction == DeviceLogDirection.In).ToList();
        var outs = group.Where(p => p.Direction == DeviceLogDirection.Out).ToList();
        var existing = await days.FindByEmployeeAndDateAsync(group[0].EmployeeId, group[0].Date, cancellationToken).ConfigureAwait(false);
        var checkIn = ins.Count > 0 ? TimeOnly.FromDateTime(ins.Min(p => p.Timestamp)) : existing?.CheckIn;
        var checkOut = outs.Count > 0 ? TimeOnly.FromDateTime(outs.Max(p => p.Timestamp)) : existing?.CheckOut;
        return checkIn is not null && checkOut is not null && checkOut <= checkIn;
    }

    /// <summary>
    /// Her satır bir okutma (yerel damga kiracı dilimiyle UTC'ye çevrilir). Günde aynı damga+yön zaten kayıtlıysa (aynı dosyanın ikinci
    /// aktarımı) yeniden eklenmez. Okutma kurulamazsa satır hatayla raporlanır ve grubun günü yeniden türetilmez.
    /// </summary>
    private static bool AddPunches(PunchDayWriter writer, List<Plan> group, IReadOnlyList<Punch> dayPunches, TimeZoneInfo zone, List<DeviceLogImportErrorDto> errors)
    {
        var failed = false;
        foreach (var plan in group)
        {
            var occurredAtUtc = Punch.NormalizeOccurredAt(PunchTime.ToUtc(plan.Timestamp, zone));
            var direction = plan.Direction == DeviceLogDirection.In ? PunchDirection.In : PunchDirection.Out;
            if (dayPunches.Any(p => p.OccurredAtUtc == occurredAtUtc && p.Direction == direction && p.Source == PunchSource.Device))
            {
                continue;
            }

            // Cihaz aktarımında ileri tarih kontrolü bugüne kadar yoktu; okutmanın kendi anına göre doğrulanır (davranış korunur).
            var added = writer.Add(plan.EmployeeId, occurredAtUtc, plan.Date, direction, PunchSource.Device, null, null, null, null, null, false, null, null, occurredAtUtc);
            if (added.IsFailure)
            {
                errors.Add(new DeviceLogImportErrorDto(plan.Line, added.Error.Code, null));
                failed = true;
            }
        }

        return failed;
    }
}
