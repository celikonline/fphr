using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.Attendance.Application.Import;

/// <summary>
/// Applies "attendance.pdks" rows. Every row is validated (employee match, date, time format, checkout &gt; checkin)
/// before anything is touched; then valid rows upsert an <see cref="AttendanceDay"/> per (employee, date) with
/// Source=Import, same as a manual <see cref="RecordAttendanceCommand"/>. AllOrNothing applies nothing when any row fails.
/// </summary>
public sealed class AttendanceImportRowHandler(
    ITenantContext tenant, IEmployeeLookup employeeLookup, IPlannedDayResolver planner, IAttendanceDayRepository days, IAttendanceUnitOfWork unitOfWork,
    IPunchRepository punches, ITenantLocalClock clock, IMonthlyTimesheetApprovalRepository approvals)
    : IImportRowHandler
{
    /// <summary>İçe aktarılan satır günün önceki okutmalarının yerini aldığında silinen okutmalara yazılan gerekçe.</summary>
    public const string ReplacedReason = "PDKS Excel içe aktarımıyla değiştirildi";

    public string TemplateCode => AttendanceImportColumns.TemplateCode;

    public async Task<ImportBatchResult> ProcessAsync(ImportBatch batch, CancellationToken cancellationToken = default)
    {
        // Z0.1: her satır Source=Import okutmalara dönüşür ve günü (üzerine yazarak) yeniden türetir; K6 kilitli ay satır hatasıdır.
        var now = await clock.NowAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        var zone = await clock.GetTimeZoneAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
        var writer = new PunchDayWriter(tenant.TenantId, days, punches, planner, approvals, zone, now.Utc);
        var employeeCache = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        var results = new Dictionary<int, ImportRowResult>();
        var plans = new List<AttendanceImportPlan>();

        foreach (var row in batch.Rows)
        {
            var errors = new List<ImportRowError>();

            var nationalId = row.Get(AttendanceImportColumns.NationalId)?.Text?.Trim();
            if (string.IsNullOrEmpty(nationalId))
            {
                errors.Add(new ImportRowError(AttendanceImportColumns.NationalId, ImportErrorCodes.RequiredMissing));
            }

            var date = ParseDate(row, errors);
            var checkIn = ParseTime(row, AttendanceImportColumns.CheckIn, errors);
            var checkOut = ParseTime(row, AttendanceImportColumns.CheckOut, errors);
            if (checkIn is not null && checkOut is not null && checkOut <= checkIn)
            {
                errors.Add(new ImportRowError(AttendanceImportColumns.CheckOut, AttendanceImportErrorCodes.CheckOutBeforeCheckIn));
            }

            Guid? employeeId = null;
            if (nationalId is not null)
            {
                if (!employeeCache.TryGetValue(nationalId, out employeeId))
                {
                    employeeId = await employeeLookup.FindActiveByNationalIdAsync(tenant.TenantId, nationalId, cancellationToken).ConfigureAwait(false);
                    employeeCache[nationalId] = employeeId;
                }

                if (employeeId is null)
                {
                    errors.Add(new ImportRowError(AttendanceImportColumns.NationalId, ImportErrorCodes.NoMatch));
                }
            }

            if (errors.Count == 0 && employeeId is { } matched && date is { } day && await writer.IsLockedAsync(matched, day, cancellationToken).ConfigureAwait(false))
            {
                errors.Add(new ImportRowError(AttendanceImportColumns.Date, AttendanceErrors.PeriodLocked));
            }

            if (errors.Count > 0 || employeeId is null || date is null)
            {
                results[row.RowNumber] = new ImportRowResult(row.RowNumber, ImportRowOutcome.Error, nationalId, employeeId, errors);
                continue;
            }

            plans.Add(new AttendanceImportPlan(row.RowNumber, nationalId!, employeeId.Value, date.Value, checkIn, checkOut));
        }

        if (batch.FailurePolicy == ImportFailurePolicy.AllOrNothing && results.Count > 0)
        {
            foreach (var plan in plans)
            {
                results[plan.RowNumber] = new ImportRowResult(plan.RowNumber, ImportRowOutcome.Skipped, plan.NationalId, null, []);
            }

            return new ImportBatchResult(Ordered(batch, results), Applied: false);
        }

        var anyChange = false;
        foreach (var plan in plans)
        {
            var wasExisting = await days.FindByEmployeeAndDateAsync(plan.EmployeeId, plan.Date, cancellationToken).ConfigureAwait(false) is not null;
            var upserted = await writer.ReplaceDayAsync(
                plan.EmployeeId, plan.Date, plan.CheckIn, plan.CheckOut, AttendanceDaySource.Import, actorUserId: null, ReplacedReason, limits: null,
                cancellationToken).ConfigureAwait(false);
            if (upserted.IsFailure)
            {
                results[plan.RowNumber] = new ImportRowResult(
                    plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, null,
                    [new ImportRowError(AttendanceImportColumns.CheckOut, AttendanceImportErrorCodes.CheckOutBeforeCheckIn)]);
                continue;
            }

            results[plan.RowNumber] = new ImportRowResult(
                plan.RowNumber, wasExisting ? ImportRowOutcome.Updated : ImportRowOutcome.Inserted, plan.NationalId, upserted.Value.Id, []);
            anyChange = true;
        }

        if (anyChange && (await writer.SaveAsync(unitOfWork, cancellationToken).ConfigureAwait(false)).IsFailure)
        {
            // Eşzamanlı yazma çakışması yeniden denemelere rağmen çözülemedi: hiçbir satır uygulanmadı.
            foreach (var plan in plans.Where(p => results[p.RowNumber].Outcome != ImportRowOutcome.Error))
            {
                results[plan.RowNumber] = new ImportRowResult(
                    plan.RowNumber, ImportRowOutcome.Error, plan.NationalId, null,
                    [new ImportRowError(AttendanceImportColumns.Date, AttendanceErrors.DayConcurrentUpdate)]);
            }

            return new ImportBatchResult(Ordered(batch, results), Applied: false);
        }

        return new ImportBatchResult(Ordered(batch, results), Applied: true);
    }

    private static DateOnly? ParseDate(ImportRow row, List<ImportRowError> errors)
    {
        var cell = row.Get(AttendanceImportColumns.Date);
        if (cell is null)
        {
            errors.Add(new ImportRowError(AttendanceImportColumns.Date, ImportErrorCodes.RequiredMissing));
            return null;
        }

        if (cell.DateValue is { } dateValue)
        {
            return DateOnly.FromDateTime(dateValue);
        }

        if (DateOnly.TryParse(cell.Text, out var parsed))
        {
            return parsed;
        }

        errors.Add(new ImportRowError(AttendanceImportColumns.Date, ImportErrorCodes.InvalidDate));
        return null;
    }

    private static TimeOnly? ParseTime(ImportRow row, string key, List<ImportRowError> errors)
    {
        var cell = row.Get(key);
        if (cell is null)
        {
            return null;
        }

        if (cell.DateValue is { } dateValue)
        {
            return TimeOnly.FromDateTime(dateValue);
        }

        if (TimeOnly.TryParse(cell.Text, out var parsed))
        {
            return parsed;
        }

        errors.Add(new ImportRowError(key, AttendanceImportErrorCodes.InvalidTime, new Dictionary<string, string> { [ImportErrorCodes.ValueArg] = cell.Text ?? string.Empty }));
        return null;
    }

    private static IReadOnlyList<ImportRowResult> Ordered(ImportBatch batch, Dictionary<int, ImportRowResult> results) =>
        batch.Rows.Where(r => results.ContainsKey(r.RowNumber)).Select(r => results[r.RowNumber]).ToList();
}

/// <summary>A valid row, ready to upsert an <see cref="AttendanceDay"/>.</summary>
internal sealed record AttendanceImportPlan(int RowNumber, string NationalId, Guid EmployeeId, DateOnly Date, TimeOnly? CheckIn, TimeOnly? CheckOut);
